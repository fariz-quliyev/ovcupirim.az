using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Stores;

public interface IStoreFollowService
{
    Task<Result> FollowAsync(string slug, CancellationToken cancellationToken = default);

    Task<Result> UnfollowAsync(string slug, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<StoreCardDto>>> GetMineAsync(PageRequest page, CancellationToken cancellationToken = default);
}

/// <summary>
/// "İzləyici ol". Both writes are idempotent, exactly like saving a listing.
/// </summary>
/// <remarks>
/// <see cref="Store.FollowerCount"/> is not written here. It is recomputed by the maintenance pass
/// with SQL that leaves <see cref="Store.Version"/> alone, so following a storefront can never
/// invalidate an edit its owner has open — the same discipline the listing counters follow.
/// </remarks>
public sealed class StoreFollowService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    IDateTimeProvider clock) : IStoreFollowService
{
    public async Task<Result> FollowAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        // Only a storefront a visitor could actually have seen can be followed.
        var storeId = await db.Stores.AsNoTracking()
            .Where(s => s.Slug == slug && s.Status == StoreStatus.Active)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (storeId is null)
        {
            return Result.NotFound(StoreService.NotFoundMessage);
        }

        if (await db.StoreFollows.AnyAsync(f => f.UserId == userId && f.StoreId == storeId, cancellationToken))
        {
            return Result.Success();
        }

        var follow = new StoreFollow
        {
            UserId = userId,
            StoreId = storeId.Value,
            CreatedAt = clock.UtcNow
        };

        db.StoreFollows.Add(follow);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The write failed. Detach it so the context is usable, then read the row back: if the
            // follow is there, two taps raced and the composite key caught the second, which is
            // the state the caller asked for anyway. Anything else is a real failure and must
            // surface rather than being reported as a follow that never happened.
            // Remove on an entity that is still in the Added state stops tracking it outright,
            // which is what leaves the context usable for the read below.
            db.StoreFollows.Remove(follow);

            if (!await AlreadyFollowsAsync(userId, storeId.Value, cancellationToken))
            {
                throw;
            }
        }

        return Result.Success();
    }

    private Task<bool> AlreadyFollowsAsync(Guid userId, Guid storeId, CancellationToken cancellationToken) =>
        db.StoreFollows.AsNoTracking()
            .AnyAsync(f => f.UserId == userId && f.StoreId == storeId, cancellationToken);

    public async Task<Result> UnfollowAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var follow = await db.StoreFollows
            .FirstOrDefaultAsync(f => f.UserId == userId && f.Store.Slug == slug, cancellationToken);

        if (follow is null)
        {
            return Result.Success();
        }

        db.StoreFollows.Remove(follow);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<PagedResult<StoreCardDto>>> GetMineAsync(
        PageRequest page, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<PagedResult<StoreCardDto>>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        // A followed storefront that is no longer public simply drops out of the list.
        var followed = db.StoreFollows.AsNoTracking()
            .Where(f => f.UserId == userId && f.Store.Status == StoreStatus.Active)
            .OrderByDescending(f => f.CreatedAt);

        var total = await followed.CountAsync(cancellationToken);

        var items = await followed
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(f => new StoreCardDto(
                f.Store.Slug,
                f.Store.Name,
                f.Store.LogoStorageKey,
                f.Store.IsVerified,
                f.Store.ListingCount,
                f.Store.FollowerCount,
                f.Store.CreatedAt))
            .ToListAsync(cancellationToken);

        var cards = items
            .Select(c => c with { LogoUrl = c.LogoUrl is null ? null : storage.GetPublicUrl(c.LogoUrl) })
            .ToList();

        return Result<PagedResult<StoreCardDto>>.Success(
            new PagedResult<StoreCardDto>(cards, page.Page, page.PageSize, total));
    }
}
