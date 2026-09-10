using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings.Search;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Application.Listings;

public interface IFavoriteService
{
    Task<Result<PagedResult<ListingCardDto>>> GetMineAsync(
        PageRequest page, CancellationToken cancellationToken = default);

    Task<Result> AddAsync(long shortId, CancellationToken cancellationToken = default);

    Task<Result> RemoveAsync(long shortId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Saved listings. Both writes are idempotent — a double tap on the heart, or a retry after a
/// dropped connection, ends in the same state rather than an error.
/// </summary>
/// <remarks>
/// The listing's <c>FavoriteCount</c> is not written here. It is recomputed by the maintenance
/// pass along with the other denormalised counters, which keeps the toggle off the listing row
/// entirely: a save can never collide with the seller editing the same listing, and a missed
/// increment cannot accumulate into a wrong number.
/// </remarks>
public sealed class FavoriteService(
    IAppDbContext db,
    IListingSearchService search,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IFavoriteService
{
    public async Task<Result<PagedResult<ListingCardDto>>> GetMineAsync(
        PageRequest page, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<PagedResult<ListingCardDto>>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var query = db.Favorites.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt);

        var total = await query.CountAsync(cancellationToken);

        var ids = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(f => f.ListingId)
            .ToListAsync(cancellationToken);

        // The same card shape and the same hydration the catalogue uses.
        var cards = await search.CardsAsync(ids, cancellationToken);

        return Result<PagedResult<ListingCardDto>>.Success(
            new PagedResult<ListingCardDto>(cards, page.Page, page.PageSize, total));
    }

    public async Task<Result> AddAsync(long shortId, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        // Addressed by the public number, so the internal id never has to appear in a card payload.
        // Only something a visitor could actually have seen can be saved.
        var listingId = await db.Listings.AsNoTracking()
            .Where(l => l.ShortId == shortId && l.Status == Domain.Enums.ListingStatus.Active)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (listingId is null)
        {
            return Result.NotFound(ListingService.NotFoundMessage);
        }

        var already = await db.Favorites
            .AnyAsync(f => f.UserId == userId && f.ListingId == listingId, cancellationToken);

        if (already)
        {
            return Result.Success();
        }

        db.Favorites.Add(new Favorite
        {
            UserId = userId,
            ListingId = listingId.Value,
            CreatedAt = clock.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two taps raced for the same row; the composite key caught it and the desired state
            // is already in place.
            return Result.Success();
        }

        return Result.Success();
    }

    public async Task<Result> RemoveAsync(long shortId, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var favorite = await db.Favorites
            .FirstOrDefaultAsync(f => f.UserId == userId && f.Listing.ShortId == shortId, cancellationToken);

        if (favorite is null)
        {
            return Result.Success();
        }

        db.Favorites.Remove(favorite);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

}
