using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Stores;

public interface IStoreAdminService
{
    Task<Result<PagedResult<StoreAdminDto>>> GetQueueAsync(
        string? status, PageRequest page, CancellationToken cancellationToken = default);

    Task<Result> ApproveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Turns down an application and removes it, so the seller may apply again.</summary>
    Task<Result> RejectAsync(Guid id, string reason, CancellationToken cancellationToken = default);

    Task<Result> SuspendAsync(Guid id, string reason, CancellationToken cancellationToken = default);

    Task<Result> ReinstateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> SetVerifiedAsync(Guid id, bool verified, CancellationToken cancellationToken = default);
}

/// <summary>
/// Administrative control of storefronts. Every decision writes an audit row naming the actor and,
/// where one is required, the reason.
/// </summary>
public sealed class StoreAdminService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IStoreAdminService
{
    public async Task<Result<PagedResult<StoreAdminDto>>> GetQueueAsync(
        string? status, PageRequest page, CancellationToken cancellationToken = default)
    {
        // Applications awaiting a decision are what a moderator opens this for. A status that was
        // asked for but not understood is refused: silently showing a different queue than the one
        // requested is how a moderator comes to believe a queue is empty.
        var wanted = StoreStatus.PendingVerification;

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status, ignoreCase: true, out wanted) || !Enum.IsDefined(wanted))
            {
                return Result<PagedResult<StoreAdminDto>>.Invalid("status", "Belə status yoxdur.");
            }
        }

        var query = db.Stores.AsNoTracking().Where(s => s.Status == wanted);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(s => s.CreatedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(s => new StoreAdminDto(
                s.Id,
                s.Slug,
                s.Name,
                s.Description,
                s.Address,
                s.Phone,
                s.Status.ToString(),
                s.IsVerified,
                s.OwnerUser.FullName,
                s.OwnerUserId,
                s.ListingCount,
                s.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<PagedResult<StoreAdminDto>>.Success(
            new PagedResult<StoreAdminDto>(items, page.Page, page.PageSize, total));
    }

    public async Task<Result> ApproveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var found = await LoadAsync(id, cancellationToken);

        if (!found.Succeeded)
        {
            return found;
        }

        var store = found.Value!;

        if (store.Status == StoreStatus.Active)
        {
            return Result.Conflict("Bu mağaza artıq aktivdir.");
        }

        // Approval belongs to an application awaiting a decision. A suspended storefront was taken
        // down for a reason, so it comes back only through reinstate — which is also what keeps the
        // audit trail honest about which of the two decisions was actually made.
        if (store.Status != StoreStatus.PendingVerification)
        {
            return Result.Conflict("Dayandırılmış mağaza yalnız bərpa yolu ilə aktivləşdirilə bilər.");
        }

        store.Status = StoreStatus.Active;

        return await RecordAsync(store, "store.approved", null, cancellationToken);
    }

    public async Task<Result> RejectAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Invalid("reason", "Rədd səbəbi tələb olunur.");
        }

        var found = await LoadAsync(id, cancellationToken);

        if (!found.Succeeded)
        {
            return found;
        }

        var store = found.Value!;

        if (store.Status != StoreStatus.PendingVerification)
        {
            return Result.Conflict("Yalnız təsdiq gözləyən müraciət rədd edilə bilər.");
        }

        // There is no Rejected status, and inventing one would leave the seller permanently
        // blocked by the one-store-per-account index. The application is withdrawn instead, so the
        // seller can correct it and apply again; the decision survives in the audit trail.
        var record = await RecordAsync(store, "store.rejected", reason.Trim(), cancellationToken, save: false);

        if (!record.Succeeded)
        {
            return record;
        }

        db.Stores.Remove(store);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> SuspendAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Invalid("reason", "Dayandırma səbəbi tələb olunur.");
        }

        var found = await LoadAsync(id, cancellationToken);

        if (!found.Succeeded)
        {
            return found;
        }

        var store = found.Value!;

        if (store.Status != StoreStatus.Active)
        {
            return Result.Conflict("Yalnız aktiv mağaza dayandırıla bilər.");
        }

        // The shopfront closes; the goods do not. The owner's listings stay live and simply stop
        // offering any way through to the store.
        store.Status = StoreStatus.Suspended;

        return await RecordAsync(store, "store.suspended", reason.Trim(), cancellationToken);
    }

    public async Task<Result> ReinstateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var found = await LoadAsync(id, cancellationToken);

        if (!found.Succeeded)
        {
            return found;
        }

        var store = found.Value!;

        if (store.Status != StoreStatus.Suspended)
        {
            return Result.Conflict("Yalnız dayandırılmış mağaza bərpa edilə bilər.");
        }

        store.Status = StoreStatus.Active;

        return await RecordAsync(store, "store.reinstated", null, cancellationToken);
    }

    public async Task<Result> SetVerifiedAsync(Guid id, bool verified, CancellationToken cancellationToken = default)
    {
        var found = await LoadAsync(id, cancellationToken);

        if (!found.Succeeded)
        {
            return found;
        }

        var store = found.Value!;

        // The badge is a statement about a storefront the public can see. Granting one to an
        // application still awaiting a decision would have it go live already wearing a badge.
        if (verified && store.Status != StoreStatus.Active)
        {
            return Result.Conflict("Yalnız aktiv mağaza təsdiqlənmiş sayıla bilər.");
        }

        store.IsVerified = verified;

        return await RecordAsync(store, verified ? "store.verified" : "store.unverified", null, cancellationToken);
    }

    private async Task<Result<Store>> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        return store is null
            ? Result<Store>.NotFound(StoreService.NotFoundMessage)
            : Result<Store>.Success(store);
    }

    /// <summary>
    /// Writes the audit row. The store's name and slug go into the payload because a rejection
    /// deletes the row, and a decision that leaves no trace of what it was about is not a decision.
    /// </summary>
    private async Task<Result> RecordAsync(
        Store store, string action, string? reason, CancellationToken cancellationToken, bool save = true)
    {
        var actorId = currentUser.UserId
            ?? throw new InvalidOperationException("Store moderation requires an authenticated administrator.");


        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            ActorUserId = actorId,
            EntityType = nameof(Store),
            EntityId = store.Id.ToString(),
            Action = action,
            PayloadJson = AuditPayload.Named(store.Name, store.Slug, reason),
            CreatedAt = clock.UtcNow
        });

        if (!save)
        {
            return Result.Success();
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Conflict("Mağaza başqa yerdə dəyişdirilib. Növbəni yeniləyin.");
        }
    }
}
