using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Listings;

public sealed record SubmitReportRequest(string Reason, string? Comment);

public sealed record ReportDto(
    Guid Id,
    Guid ListingId,
    long ListingShortId,
    string ListingTitle,
    string Reason,
    string? Comment,
    string Status,
    bool IsAnonymous,
    DateTimeOffset CreatedAt);

public interface IListingReportService
{
    /// <summary>"Şikayət et" on a public listing. Accepted from signed-out visitors.</summary>
    Task<Result> SubmitAsync(long shortId, SubmitReportRequest request, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<ReportDto>>> GetQueueAsync(
        string? status, PageRequest page, CancellationToken cancellationToken = default);

    Task<Result> ResolveAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> DismissAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reader-side of moderation: what visitors flag, and the queue a moderator works through.
/// Blocking a reported listing goes through the existing Phase 4 moderation service, so there is
/// only ever one path that changes a listing's status.
/// </summary>
public sealed class ListingReportService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IListingReportService
{
    public async Task<Result> SubmitAsync(
        long shortId, SubmitReportRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<ReportReason>(request.Reason, ignoreCase: true, out var reason))
        {
            return Result.Invalid("reason", "Şikayət səbəbi düzgün deyil.");
        }

        var listingId = await db.Listings.AsNoTracking()
            .Where(l => l.ShortId == shortId && l.Status == ListingStatus.Active)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (listingId is null)
        {
            // The same answer a missing listing gives, so a report cannot be used to probe for
            // listings in a non-public state.
            return Result.NotFound(ListingService.NotFoundMessage);
        }

        var reporterId = currentUser.UserId;

        if (reporterId is { } userId)
        {
            // One open report per person per listing; repeat taps do not inflate the queue.
            var already = await db.Reports.AnyAsync(
                r => r.ListingId == listingId && r.ReporterUserId == userId && r.Status == ReportStatus.Open,
                cancellationToken);

            if (already)
            {
                return Result.Success();
            }
        }

        db.Reports.Add(new Report
        {
            Id = Guid.CreateVersion7(),
            ListingId = listingId.Value,
            ReporterUserId = reporterId,
            Reason = reason,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            Status = ReportStatus.Open,
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<PagedResult<ReportDto>>> GetQueueAsync(
        string? status, PageRequest page, CancellationToken cancellationToken = default)
    {
        var query = db.Reports.AsNoTracking();

        // Default to what still needs a decision.
        var wanted = Enum.TryParse<ReportStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : ReportStatus.Open;

        query = query.Where(r => r.Status == wanted);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(r => r.CreatedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(r => new ReportDto(
                r.Id,
                r.ListingId,
                r.Listing.ShortId,
                r.Listing.Title,
                r.Reason.ToString(),
                r.Comment,
                r.Status.ToString(),
                r.ReporterUserId == null,
                r.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<PagedResult<ReportDto>>.Success(
            new PagedResult<ReportDto>(items, page.Page, page.PageSize, total));
    }

    public Task<Result> ResolveAsync(Guid id, CancellationToken cancellationToken = default) =>
        CloseAsync(id, ReportStatus.Resolved, cancellationToken);

    public Task<Result> DismissAsync(Guid id, CancellationToken cancellationToken = default) =>
        CloseAsync(id, ReportStatus.Dismissed, cancellationToken);

    private async Task<Result> CloseAsync(Guid id, ReportStatus status, CancellationToken cancellationToken)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (report is null)
        {
            return Result.NotFound("Şikayət tapılmadı.");
        }

        if (report.Status is ReportStatus.Resolved or ReportStatus.Dismissed)
        {
            return Result.Conflict("Bu şikayət artıq bağlanıb.");
        }

        var now = clock.UtcNow;

        report.Status = status;
        report.ResolvedByUserId = currentUser.UserId;
        report.ResolvedAt = now;

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            ActorUserId = currentUser.UserId,
            EntityType = nameof(Report),
            EntityId = report.Id.ToString(),
            Action = $"report.{status.ToString().ToLowerInvariant()}",
            CreatedAt = now
        });

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
