using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Application.Notifications;

/// <summary>One notification to deliver, independent of how it gets there.</summary>
public sealed record NotificationMessage(
    Guid UserId,
    string Type,
    string Title,
    string Body,
    string? EntityType = null,
    string? EntityId = null);

/// <summary>
/// One delivery mechanism. Today only <see cref="InAppNotificationChannel"/> is registered; a push,
/// email or SMS channel is a second implementation of this same interface, added to DI alongside
/// the first — nothing about <see cref="INotificationService"/> or its callers changes to add one.
/// </summary>
/// <remarks>
/// A channel that only writes to this database (the in-app one) should stage the write on the
/// ambient <see cref="IAppDbContext"/> and let its caller's own <c>SaveChangesAsync</c> commit it —
/// which is what lets a notification land in the same transaction as the decision that caused it,
/// the same way an <c>AuditLog</c> row does. A channel that actually dispatches over the network has
/// no such transaction to join and is expected to complete its own delivery (or hand off to a queue)
/// within this call.
/// </remarks>
public interface INotificationChannel
{
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    /// <summary>Fans one message out to every registered channel. Callers never address a channel directly.</summary>
    Task NotifyAsync(NotificationMessage message, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<NotificationDto>>> GetMineAsync(
        PageRequest page, CancellationToken cancellationToken = default);

    Task<Result<UnreadNotificationCountDto>> GetUnreadCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Idempotent: marking an already-read notification read again succeeds and changes nothing.</summary>
    Task<Result> MarkReadAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> MarkAllReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Writes the notification row. The transaction is whichever caller's SaveChangesAsync runs next.</summary>
public sealed class InAppNotificationChannel(IAppDbContext db, IDateTimeProvider clock) : INotificationChannel
{
    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(),
            UserId = message.UserId,
            Type = message.Type,
            Title = message.Title,
            Body = message.Body,
            EntityType = message.EntityType,
            EntityId = message.EntityId,
            CreatedAt = clock.UtcNow
        });

        return Task.CompletedTask;
    }
}

public sealed class NotificationService(
    IAppDbContext db,
    IEnumerable<INotificationChannel> channels,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : INotificationService
{
    public async Task NotifyAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        foreach (var channel in channels)
        {
            await channel.SendAsync(message, cancellationToken);
        }
    }

    public async Task<Result<PagedResult<NotificationDto>>> GetMineAsync(
        PageRequest page, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<PagedResult<NotificationDto>>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var query = db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(n => new NotificationDto(
                n.Id, n.Type, n.Title, n.Body, n.EntityType, n.EntityId, n.ReadAt != null, n.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<PagedResult<NotificationDto>>.Success(
            new PagedResult<NotificationDto>(items, page.Page, page.PageSize, total));
    }

    public async Task<Result<UnreadNotificationCountDto>> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<UnreadNotificationCountDto>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var count = await db.Notifications.AsNoTracking()
            .CountAsync(n => n.UserId == userId && n.ReadAt == null, cancellationToken);

        return Result<UnreadNotificationCountDto>.Success(new UnreadNotificationCountDto(count));
    }

    public async Task<Result> MarkReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        // Owner-scoped in the query itself, not checked afterwards: a notification addressed to
        // someone else is not found, not forbidden — the same shape ordinary listing access uses so
        // one seller's account can never be used to probe whether an id belongs to another.
        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);

        if (notification is null)
        {
            return Result.NotFound("Bildiriş tapılmadı.");
        }

        if (notification.ReadAt is null)
        {
            notification.ReadAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var now = clock.UtcNow;

        // Load-then-set rather than ExecuteUpdateAsync: the in-memory provider the rest of this
        // application's tests run against cannot translate a set-based update, and one seller's
        // unread notifications are never numerous enough for the round trip to matter. The same
        // shape AuthService.RevokeAllForUserAsync already uses for the same reason.
        var unread = await db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ToListAsync(cancellationToken);

        foreach (var notification in unread)
        {
            notification.ReadAt = now;
        }

        if (unread.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
