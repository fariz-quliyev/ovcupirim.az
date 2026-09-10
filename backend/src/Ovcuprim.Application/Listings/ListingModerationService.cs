using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Categories;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Notifications;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Listings;

public interface IListingModerationService
{
    /// <summary>
    /// The moderation queue for one status. <paramref name="strict"/> narrows to restricted and
    /// unclassified categories. <paramref name="status"/> selects which listings are eligible —
    /// <c>"pending"</c> (the default) for the ordinary approve/reject queue, or <c>"active"</c> for
    /// the smaller set of live listings a moderator can still block. No other status is browsable
    /// here: a listing's history is available a decision at a time from
    /// <see cref="GetForModerationAsync"/>, and a general status browser is not what this queue is for.
    /// </summary>
    Task<Result<PagedResult<ModerationQueueItemDto>>> GetQueueAsync(
        bool? strict, PageRequest page, string? status = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything a moderator needs to decide: the full listing in any status, with its images,
    /// description, attributes and decision history. The ordinary listing endpoint is owner-scoped,
    /// so this is the only way a moderator can see what they are approving.
    /// </summary>
    Task<Result<ModerationDetailDto>> GetForModerationAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> ApproveAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> RejectAsync(Guid id, string reason, CancellationToken cancellationToken = default);

    Task<Result> BlockAsync(Guid id, string reason, CancellationToken cancellationToken = default);

    Task<Result> UnblockAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>One decision already taken on a listing.</summary>
public sealed record ModerationHistoryDto(
    string Action,
    string? Reason,
    string ModeratorName,
    DateTimeOffset CreatedAt);

/// <summary>
/// The moderator's view of a listing.
/// </summary>
/// <remarks>
/// Carries the seller's contact number <b>unmasked</b> (PD-7.5): fraud patterns are phone-shaped,
/// and a moderator who cannot see the number cannot spot them. This is the single deliberate
/// widening beyond the public page, it is Moderator-gated, and it does not extend to a store
/// owner's account phone or email, which remain unreachable from every surface.
/// </remarks>
public sealed record ModerationDetailDto(
    ListingDetailDto Listing,
    string SellerName,
    IReadOnlyList<ModerationHistoryDto> History);

public sealed class ListingModerationService(
    IAppDbContext db,
    ICategoryService categories,
    ICurrentUser currentUser,
    IFileStorage storage,
    IDateTimeProvider clock,
    INotificationService notifications) : IListingModerationService
{
    public async Task<Result<ModerationDetailDto>> GetForModerationAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var listing = await db.Listings.AsNoTracking()
            .Include(l => l.Category)
            .Include(l => l.Region)
            .Include(l => l.Media)
            .Include(l => l.User)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

        if (listing is null)
        {
            return Result<ModerationDetailDto>.NotFound(ListingService.NotFoundMessage);
        }

        // No owner check, by design — that is what makes this a moderation view. The endpoint is
        // Moderator-gated instead, and tested for anonymous and ordinary-user access.
        var schema = (await categories.GetSchemaAsync(listing.Category.Slug, cancellationToken)).Value;

        var history = await db.ModerationActions.AsNoTracking()
            .Where(a => a.ListingId == id)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new ModerationHistoryDto(
                a.Action.ToString(),
                a.Reason,
                a.ModeratorUser.FullName,
                a.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<ModerationDetailDto>.Success(new ModerationDetailDto(
            ListingMapper.ToDetail(listing, schema, storage, clock.UtcNow),
            listing.User.FullName,
            history));
    }

    public async Task<Result<PagedResult<ModerationQueueItemDto>>> GetQueueAsync(
        bool? strict, PageRequest page, string? status = null, CancellationToken cancellationToken = default)
    {
        var wantedStatus = ListingStatus.PendingModeration;

        if (!string.IsNullOrWhiteSpace(status))
        {
            // A status that was asked for but not understood is refused rather than quietly showing
            // the pending queue instead: that is how a moderator comes to believe a queue is empty.
            // Deliberately narrower than every ListingStatus — Draft, Rejected, Expired, Sold,
            // Archived and Blocked all have a place to be seen already (the seller's own listings,
            // or a decision's own history) and none of them is a queue to browse.
            wantedStatus = status.Trim().ToLowerInvariant() switch
            {
                "pending" => ListingStatus.PendingModeration,
                "active" => ListingStatus.Active,
                _ => (ListingStatus)(-1)
            };

            if (wantedStatus == (ListingStatus)(-1))
            {
                return Result<PagedResult<ModerationQueueItemDto>>.Invalid("status", "Belə status yoxdur.");
            }
        }

        var pending = db.Listings.AsNoTracking().Where(l => l.Status == wantedStatus);

        var rows = await pending
            .Include(l => l.Category)
            .Include(l => l.User)
            .OrderBy(l => l.UpdatedAt ?? l.CreatedAt)
            .ToListAsync(cancellationToken);

        var flagsByListing = await ScreeningFlagsAsync(rows.Select(r => r.Id).ToList(), cancellationToken);
        var items = new List<ModerationQueueItemDto>(rows.Count);

        foreach (var listing in rows)
        {
            var category = (await categories.GetBySlugAsync(listing.Category.Slug, cancellationToken)).Value;
            var restriction = category?.RestrictionStatus ?? nameof(RestrictionStatus.Unclassified);

            // Restricted and Unclassified both queue strictly; the label keeps them distinct so a
            // pending classification is never presented as a legal restriction.
            var isStrict = restriction is nameof(RestrictionStatus.Restricted) or nameof(RestrictionStatus.Unclassified);

            if (strict is { } wanted && wanted != isStrict)
            {
                continue;
            }

            items.Add(new ModerationQueueItemDto(
                listing.Id,
                listing.ShortId,
                listing.Title,
                listing.Category.NameAz,
                restriction,
                isStrict,
                listing.User.FullName,
                flagsByListing.TryGetValue(listing.Id, out var flags) ? flags : [],
                listing.UpdatedAt ?? listing.CreatedAt));
        }

        var paged = items.Skip(page.Skip).Take(page.PageSize).ToList();

        return Result<PagedResult<ModerationQueueItemDto>>.Success(
            new PagedResult<ModerationQueueItemDto>(paged, page.Page, page.PageSize, items.Count));
    }

    public async Task<Result> ApproveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var found = await LoadAsync(id, cancellationToken);

        if (!found.Succeeded)
        {
            return found;
        }

        var listing = found.Value!;

        if (!ListingStateMachine.CanModerate(listing.Status))
        {
            return Result.Conflict("Yalnız gözləmədə olan elan təsdiqlənə bilər.");
        }

        var now = clock.UtcNow;

        // A listing that has never been live is starting its 30 days now. One coming back through
        // moderation after an edit is not: re-approving it must neither extend its life nor move it
        // up the page, or a seller could keep a listing on top forever by editing it. Restore and
        // renew clear ExpiresAt before resubmitting, so they land here as a fresh run and are
        // treated as one.
        var isFirstApproval = listing.PublishedAt is null || listing.ExpiresAt is null;

        listing.Status = ListingStatus.Active;
        listing.RejectionReason = null;

        if (isFirstApproval)
        {
            listing.PublishedAt ??= now;
            listing.BumpedAt = now;
            listing.ExpiresAt = now + ListingStateMachine.ActiveLifetime;
        }

        await notifications.NotifyAsync(new NotificationMessage(
            listing.UserId,
            "listing.approved",
            "Elanınız təsdiqləndi",
            $"\"{listing.Title}\" elanı təsdiqləndi və artıq saytda görünür.",
            nameof(Listing),
            listing.Id.ToString()), cancellationToken);

        return await RecordAsync(listing, ModerationActionType.Approved, null, now, cancellationToken);
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

        var listing = found.Value!;

        if (!ListingStateMachine.CanModerate(listing.Status))
        {
            return Result.Conflict("Yalnız gözləmədə olan elan rədd edilə bilər.");
        }

        var now = clock.UtcNow;

        listing.Status = ListingStatus.Rejected;
        listing.RejectionReason = reason.Trim();

        await notifications.NotifyAsync(new NotificationMessage(
            listing.UserId,
            "listing.rejected",
            "Elanınız rədd edildi",
            $"\"{listing.Title}\" elanı rədd edildi. Səbəb: {listing.RejectionReason}",
            nameof(Listing),
            listing.Id.ToString()), cancellationToken);

        return await RecordAsync(listing, ModerationActionType.Rejected, listing.RejectionReason, now, cancellationToken);
    }

    public async Task<Result> BlockAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Invalid("reason", "Bloklama səbəbi tələb olunur.");
        }

        var found = await LoadAsync(id, cancellationToken);

        if (!found.Succeeded)
        {
            return found;
        }

        var listing = found.Value!;

        if (!ListingStateMachine.CanBlock(listing.Status))
        {
            return Result.Conflict("Bu elan bloklana bilməz.");
        }

        var now = clock.UtcNow;

        listing.Status = ListingStatus.Blocked;
        listing.RejectionReason = reason.Trim();

        await notifications.NotifyAsync(new NotificationMessage(
            listing.UserId,
            "listing.blocked",
            "Elanınız bloklandı",
            $"\"{listing.Title}\" elanı bloklandı. Səbəb: {listing.RejectionReason}",
            nameof(Listing),
            listing.Id.ToString()), cancellationToken);

        return await RecordAsync(listing, ModerationActionType.Blocked, listing.RejectionReason, now, cancellationToken);
    }

    public async Task<Result> UnblockAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var found = await LoadAsync(id, cancellationToken);

        if (!found.Succeeded)
        {
            return found;
        }

        var listing = found.Value!;

        if (listing.Status != ListingStatus.Blocked)
        {
            return Result.Conflict("Yalnız bloklanmış elan açıla bilər.");
        }

        var now = clock.UtcNow;

        listing.Status = ListingStatus.Active;
        listing.RejectionReason = null;
        listing.ExpiresAt ??= now + ListingStateMachine.ActiveLifetime;

        return await RecordAsync(listing, ModerationActionType.Unblocked, null, now, cancellationToken);
    }

    private async Task<Result<Listing>> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var listing = await db.Listings.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

        return listing is null
            ? Result<Listing>.NotFound(ListingService.NotFoundMessage)
            : Result<Listing>.Success(listing);
    }

    /// <summary>Writes the append-only moderation record and the admin audit row together.</summary>
    private async Task<Result> RecordAsync(
        Listing listing, ModerationActionType action, string? reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var moderatorId = currentUser.UserId
            ?? throw new InvalidOperationException("Moderation requires an authenticated moderator.");

        db.ModerationActions.Add(new ModerationAction
        {
            Id = Guid.CreateVersion7(),
            ListingId = listing.Id,
            ModeratorUserId = moderatorId,
            Action = action,
            Reason = reason,
            CreatedAt = now
        });

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            ActorUserId = moderatorId,
            EntityType = nameof(Listing),
            EntityId = listing.Id.ToString(),
            Action = $"listing.moderation.{action.ToString().ToLowerInvariant()}",
            PayloadJson = AuditPayload.Reason(reason),
            CreatedAt = now
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Conflict("Elan başqa yerdə dəyişdirilib. Növbəni yeniləyin.");
        }
    }

    private async Task<Dictionary<Guid, IReadOnlyList<string>>> ScreeningFlagsAsync(
        List<Guid> listingIds, CancellationToken cancellationToken)
    {
        var ids = listingIds.Select(i => i.ToString()).ToList();

        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "listing.screening.flagged" && ids.Contains(a.EntityId))
            .Select(a => new { a.EntityId, a.PayloadJson })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => r.PayloadJson is not null)
            .GroupBy(r => Guid.Parse(r.EntityId))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.SelectMany(r => AuditPayload.ReadFlagMessages(r.PayloadJson)).ToList());
    }
}
