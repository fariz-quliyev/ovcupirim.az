using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Categories;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Stores;

public interface IStoreService
{
    /// <summary>Applies for a storefront. One per account, awaiting an administrator.</summary>
    Task<Result<StoreOwnerDto>> ApplyAsync(ApplyForStoreRequest request, CancellationToken cancellationToken = default);

    /// <summary>The caller's own store in any status, or NotFound when they have none.</summary>
    Task<Result<StoreOwnerDto>> GetMineAsync(CancellationToken cancellationToken = default);

    Task<Result<StoreOwnerDto>> UpdateMineAsync(UpdateStoreRequest request, CancellationToken cancellationToken = default);

    /// <summary>The public storefront. A store that is not public answers like one that is missing.</summary>
    Task<Result<StorePublicDto>> GetPublicAsync(string slug, CancellationToken cancellationToken = default);

    Task<Result<StorePhoneDto>> GetPublicPhoneAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>The id of a publicly visible storefront, or null. Used to pin the catalogue query.</summary>
    Task<Guid?> GetPublicIdAsync(string slug, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<StoreCardDto>>> GetDirectoryAsync(
        string? categorySlug, string? sort, PageRequest page, CancellationToken cancellationToken = default);
}

/// <summary>
/// Storefronts, from application to public page.
/// </summary>
/// <remarks>
/// A store is public only while it is <see cref="StoreStatus.Active"/> and its owner still exists.
/// Anything else — awaiting approval, suspended, orphaned — answers exactly like a store that never
/// existed, so a slug cannot be probed for the state of someone's application.
/// </remarks>
public sealed class StoreService(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICategoryService categories,
    IFileStorage storage,
    IDateTimeProvider clock) : IStoreService
{
    public const string NotFoundMessage = "Belə mağaza mövcud deyil.";

    public async Task<Result<StoreOwnerDto>> ApplyAsync(
        ApplyForStoreRequest request, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<StoreOwnerDto>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        // One storefront per account, which the unique index also enforces.
        if (await db.Stores.AnyAsync(s => s.OwnerUserId == userId, cancellationToken))
        {
            return Result<StoreOwnerDto>.Conflict("Bu hesabın artıq mağazası var.");
        }

        var name = request.Name.Trim();
        var phone = NormalisePhone(request.Phone);

        if (request.Phone is not null && phone is null)
        {
            return Result<StoreOwnerDto>.Invalid("phone", "Telefon nömrəsi düzgün deyil.");
        }

        var slug = await UniqueSlugAsync(name, cancellationToken);

        if (slug is null)
        {
            return Result<StoreOwnerDto>.Invalid("name", "Addan ünvan yaratmaq mümkün olmadı. Başqa ad seçin.");
        }

        var store = new Store
        {
            Id = Guid.CreateVersion7(),
            OwnerUserId = userId,
            Name = name,
            Slug = slug,
            Description = Trimmed(request.Description),
            Address = Trimmed(request.Address),
            Phone = phone,
            Status = StoreStatus.PendingVerification,
            CreatedAt = clock.UtcNow
        };

        db.Stores.Add(store);
        await db.SaveChangesAsync(cancellationToken);

        return Result<StoreOwnerDto>.Success(ToOwner(store));
    }

    public async Task<Result<StoreOwnerDto>> GetMineAsync(CancellationToken cancellationToken = default)
    {
        var found = await LoadMineAsync(cancellationToken, tracking: false);

        return found.Succeeded
            ? Result<StoreOwnerDto>.Success(ToOwner(found.Value!))
            : Result<StoreOwnerDto>.From(found);
    }

    public async Task<Result<StoreOwnerDto>> UpdateMineAsync(
        UpdateStoreRequest request, CancellationToken cancellationToken = default)
    {
        var found = await LoadMineAsync(cancellationToken);

        if (!found.Succeeded)
        {
            return Result<StoreOwnerDto>.From(found);
        }

        var store = found.Value!;
        var phone = NormalisePhone(request.Phone);

        if (request.Phone is not null && phone is null)
        {
            return Result<StoreOwnerDto>.Invalid("phone", "Telefon nömrəsi düzgün deyil.");
        }

        // The name may change; the slug it produced may not, so existing links keep working.
        store.Name = request.Name.Trim();
        store.Description = Trimmed(request.Description);
        store.Address = Trimmed(request.Address);
        store.Phone = phone;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<StoreOwnerDto>.Conflict("Mağaza başqa yerdə dəyişdirilib. Səhifəni yeniləyib yenidən cəhd edin.");
        }

        return Result<StoreOwnerDto>.Success(ToOwner(store));
    }

    public async Task<Result<StorePublicDto>> GetPublicAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        var store = await PublicStores().FirstOrDefaultAsync(s => s.Slug == slug, cancellationToken);

        if (store is null)
        {
            return Result<StorePublicDto>.NotFound(NotFoundMessage);
        }

        var isFollowing = currentUser.UserId is { } userId
            && await db.StoreFollows.AsNoTracking()
                .AnyAsync(f => f.UserId == userId && f.StoreId == store.Id, cancellationToken);

        return Result<StorePublicDto>.Success(new StorePublicDto(
            store.Slug,
            store.Name,
            store.Description,
            store.Address,
            store.IsVerified,
            Url(store.LogoStorageKey),
            Url(store.BannerStorageKey),
            store.ListingCount,
            store.FollowerCount,
            store.Phone is not null,
            store.Phone is null ? null : PhoneNumber.Mask(store.Phone),
            isFollowing,
            store.CreatedAt));
    }

    public async Task<Result<StorePhoneDto>> GetPublicPhoneAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        // Only the storefront's own number is ever published. The owner's account phone is a
        // different field and never leaves the server.
        var phone = await PublicStores()
            .Where(s => s.Slug == slug)
            .Select(s => s.Phone)
            .FirstOrDefaultAsync(cancellationToken);

        if (phone is null)
        {
            return Result<StorePhoneDto>.NotFound(NotFoundMessage);
        }

        return Result<StorePhoneDto>.Success(new StorePhoneDto(phone));
    }

    public Task<Guid?> GetPublicIdAsync(string slug, CancellationToken cancellationToken = default) =>
        PublicStores()
            .Where(s => s.Slug == slug)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Result<PagedResult<StoreCardDto>>> GetDirectoryAsync(
        string? categorySlug, string? sort, PageRequest page, CancellationToken cancellationToken = default)
    {
        var stores = PublicStores();

        if (!string.IsNullOrWhiteSpace(categorySlug))
        {
            // "Stores selling in this category" — the storefronts with at least one live listing
            // there. The category means its whole subtree, exactly as it does in the catalogue:
            // listings only ever sit in leaves, so an exact match on a parent would find nothing.
            var categoryIds = await categories.GetSubtreeIdsAsync(categorySlug, cancellationToken);

            if (categoryIds is null)
            {
                return Result<PagedResult<StoreCardDto>>.Invalid("category", "Kateqoriya tapılmadı.");
            }

            stores = stores.Where(s => db.Listings
                .Any(l => l.StoreId == s.Id
                    && categoryIds.Contains(l.CategoryId)
                    && l.Status == ListingStatus.Active));
        }

        stores = sort?.ToLowerInvariant() switch
        {
            "newest" => stores.OrderByDescending(s => s.CreatedAt),
            "listings" => stores.OrderByDescending(s => s.ListingCount),
            "followers" => stores.OrderByDescending(s => s.FollowerCount),
            _ => stores.OrderBy(s => s.Name)
        };

        var total = await stores.CountAsync(cancellationToken);

        var items = await stores
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(s => new StoreCardDto(
                s.Slug,
                s.Name,
                s.LogoStorageKey,
                s.IsVerified,
                s.ListingCount,
                s.FollowerCount,
                s.CreatedAt))
            .ToListAsync(cancellationToken);

        // The storage key becomes a URL outside the query, where the provider cannot see it.
        var cards = items
            .Select(c => c with { LogoUrl = Url(c.LogoUrl) })
            .ToList();

        return Result<PagedResult<StoreCardDto>>.Success(
            new PagedResult<StoreCardDto>(cards, page.Page, page.PageSize, total));
    }

    /// <summary>
    /// The one definition of "publicly visible". Every public store read goes through this, so a
    /// pending or suspended storefront can never leak from one endpoint while being hidden by
    /// another.
    /// </summary>
    private IQueryable<Store> PublicStores() =>
        db.Stores.AsNoTracking().Where(s => s.Status == StoreStatus.Active);

    private async Task<Result<Store>> LoadMineAsync(CancellationToken cancellationToken, bool tracking = true)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<Store>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var query = tracking ? db.Stores : db.Stores.AsNoTracking();
        var store = await query.FirstOrDefaultAsync(s => s.OwnerUserId == userId, cancellationToken);

        return store is null
            ? Result<Store>.NotFound("Sizin mağazanız yoxdur.")
            : Result<Store>.Success(store);
    }

    /// <summary>
    /// Derived from the name once, at application time, and never again. A collision takes a
    /// numeric suffix rather than failing the application.
    /// </summary>
    private async Task<string?> UniqueSlugAsync(string name, CancellationToken cancellationToken)
    {
        var baseSlug = AzerbaijaniText.ToSlug(name, 110);

        if (baseSlug.Length == 0)
        {
            return null;
        }

        var taken = await db.Stores.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(s => s.Slug == baseSlug || s.Slug.StartsWith(baseSlug + "-"))
            .Select(s => s.Slug)
            .ToListAsync(cancellationToken);

        if (!taken.Contains(baseSlug, StringComparer.Ordinal))
        {
            return baseSlug;
        }

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{baseSlug}-{suffix}";

            if (!taken.Contains(candidate, StringComparer.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private string? Url(string? key) => key is null ? null : storage.GetPublicUrl(key);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalisePhone(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : PhoneNumber.Normalize(value);

    private StoreOwnerDto ToOwner(Store store) => new(
        store.Id,
        store.Slug,
        store.Name,
        store.Description,
        store.Address,
        store.Phone,
        store.Status.ToString(),
        store.IsVerified,
        store.Status == StoreStatus.Active,
        Url(store.LogoStorageKey),
        Url(store.BannerStorageKey),
        store.ListingCount,
        store.FollowerCount,
        store.CreatedAt);
}
