using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Admin;

/// <summary>One queue's depth, and how long its oldest item has been waiting.</summary>
public sealed record QueueDepthDto(int Count, DateTimeOffset? OldestWaitingSince);

/// <summary>What was decided in the last 24 hours.</summary>
public sealed record RecentDecisionsDto(
    int ListingsApproved,
    int ListingsRejected,
    int ListingsBlocked,
    int ReportsResolved,
    int ReportsDismissed,
    int StoresApproved,
    int StoresRejected);

/// <summary>
/// <c>LateCaptures</c> counts payments the gateway confirmed only after the order had expired —
/// captured money that will never activate a promotion and is waiting for an operator to refund it.
/// Operational, not a revenue figure: PD-7.3 still holds.
/// </summary>
public sealed record AdminOverviewDto(
    QueueDepthDto PendingListings,
    QueueDepthDto StrictPendingListings,
    QueueDepthDto OpenReports,
    QueueDepthDto PendingStores,
    QueueDepthDto LateCaptures,
    RecentDecisionsDto Last24Hours,
    bool DatabaseReachable,
    DateTimeOffset GeneratedAt);

public interface IAdminOverviewService
{
    Task<Result<AdminOverviewDto>> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The operations landing page: how much work is waiting, how long it has been waiting, and what
/// was decided recently.
/// </summary>
/// <remarks>
/// Deliberately operational only (PD-7.3). No revenue, no conversion, no seller ranking — the
/// numbers here answer "is the site being kept up with", and nothing else. Every count runs against
/// an index that already exists, and every read is <c>AsNoTracking</c>.
/// </remarks>
public sealed class AdminOverviewService(IAppDbContext db, IDateTimeProvider clock) : IAdminOverviewService
{
    public async Task<Result<AdminOverviewDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var since = now.AddHours(-24);

        var pending = db.Listings.AsNoTracking().Where(l => l.Status == ListingStatus.PendingModeration);

        // "Strict" means the categories a moderator must look at hardest: restricted, plus those
        // whose classification is still pending. Same definition the queue endpoint uses.
        var strict = pending.Where(l =>
            l.Category.RestrictionStatus == RestrictionStatus.Restricted
            || l.Category.RestrictionStatus == RestrictionStatus.Unclassified);

        var openReports = db.Reports.AsNoTracking().Where(r => r.Status == ReportStatus.Open);
        var pendingStores = db.Stores.AsNoTracking().Where(s => s.Status == StoreStatus.PendingVerification);
        var lateCaptures = db.PaymentOrders.AsNoTracking().Where(o => o.Status == PaymentOrderStatus.PaidAfterExpiry);

        var moderation = db.ModerationActions.AsNoTracking().Where(a => a.CreatedAt >= since);
        var audits = db.AuditLogs.AsNoTracking().Where(a => a.CreatedAt >= since);

        var overview = new AdminOverviewDto(
            // Waiting since the listing was submitted: UpdatedAt when it has been touched, else created.
            await DepthAsync(pending.Select(l => l.UpdatedAt ?? (DateTimeOffset?)l.CreatedAt), cancellationToken),
            await DepthAsync(strict.Select(l => l.UpdatedAt ?? (DateTimeOffset?)l.CreatedAt), cancellationToken),
            await DepthAsync(openReports.Select(r => (DateTimeOffset?)r.CreatedAt), cancellationToken),
            await DepthAsync(pendingStores.Select(st => (DateTimeOffset?)st.CreatedAt), cancellationToken),
            await DepthAsync(lateCaptures.Select(o => (DateTimeOffset?)o.CreatedAt), cancellationToken),
            new RecentDecisionsDto(
                await moderation.CountAsync(a => a.Action == ModerationActionType.Approved, cancellationToken),
                await moderation.CountAsync(a => a.Action == ModerationActionType.Rejected, cancellationToken),
                await moderation.CountAsync(a => a.Action == ModerationActionType.Blocked, cancellationToken),
                await audits.CountAsync(a => a.Action == "report.resolved", cancellationToken),
                await audits.CountAsync(a => a.Action == "report.dismissed", cancellationToken),
                await audits.CountAsync(a => a.Action == "store.approved", cancellationToken),
                await audits.CountAsync(a => a.Action == "store.rejected", cancellationToken)),
            await DatabaseReachableAsync(cancellationToken),
            now);

        return Result<AdminOverviewDto>.Success(overview);
    }

    private static async Task<QueueDepthDto> DepthAsync(
        IQueryable<DateTimeOffset?> waitingSince, CancellationToken cancellationToken)
    {
        var count = await waitingSince.CountAsync(cancellationToken);

        // The number that actually tells an operator whether the queue is being kept up with.
        var oldest = count == 0 ? null : await waitingSince.MinAsync(cancellationToken);

        return new QueueDepthDto(count, oldest);
    }

    /// <summary>
    /// A cheap round trip. The full readiness check lives at <c>/health</c>; this is the one
    /// indicator the dashboard needs so an operator is not left guessing why a queue is empty.
    /// </summary>
    private async Task<bool> DatabaseReachableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.Users.AsNoTracking().Select(u => u.Id).FirstOrDefaultAsync(cancellationToken);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
