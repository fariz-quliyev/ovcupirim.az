using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Categories;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Regions;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Listings;

public interface IListingService
{
    Task<Result<ListingDetailDto>> CreateDraftAsync(CreateListingRequest request, CancellationToken cancellationToken = default);

    Task<Result<ListingDetailDto>> UpdateAsync(Guid id, UpdateListingRequest request, CancellationToken cancellationToken = default);

    Task<Result<ListingDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<ListingSummaryDto>>> GetMineAsync(string? bucket, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>
    /// The buyer-facing page. Restricted and unclassified categories are browsable, but opening one
    /// requires an explicit age acknowledgement — an OvcuPrim product rule, not a legal claim.
    /// </summary>
    Task<Result<ListingPublicDto>> GetPublicAsync(
        long shortId, bool ageConfirmed, CancellationToken cancellationToken = default);

    Task<Result<ListingPhoneDto>> GetPublicPhoneAsync(long shortId, CancellationToken cancellationToken = default);

    Task<Result<ListingDetailDto>> MarkSoldAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Seller removal: a draft is deleted outright, anything published retires to Expired.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class ListingService(
    IAppDbContext db,
    ICategoryService categories,
    IRegionService regions,
    IAttributeValidator attributeValidator,
    ICurrentUser currentUser,
    IFileStorage storage,
    IViewCountBuffer viewCounts,
    IDateTimeProvider clock) : IListingService
{
    internal const string NotFoundMessage = "Bu nömrəli elan mövcud deyil və ya ləğv olunub.";

    public async Task<Result<ListingDetailDto>> CreateDraftAsync(
        CreateListingRequest request, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<ListingDetailDto>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var schemaResult = await categories.GetSchemaAsync(request.CategorySlug, cancellationToken);

        if (!schemaResult.Succeeded)
        {
            return Result<ListingDetailDto>.Invalid("categorySlug", "Kateqoriya tapılmadı.");
        }

        var schema = schemaResult.Value!;

        if (!schema.Category.IsLeaf)
        {
            return Result<ListingDetailDto>.Invalid("categorySlug", "Elan yalnız alt kateqoriyaya yerləşdirilə bilər.");
        }

        var region = await ResolveRegionAsync(request.RegionSlug, cancellationToken);

        if (!region.Succeeded)
        {
            return Result<ListingDetailDto>.From(region);
        }

        var attributes = attributeValidator.Validate(schema.Attributes, request.Attributes);

        if (!attributes.IsValid)
        {
            return Result<ListingDetailDto>.Invalid(attributes.Errors);
        }

        var phone = PhoneNumber.Normalize(request.ContactPhone);

        if (phone is null)
        {
            return Result<ListingDetailDto>.Invalid("contactPhone", "Telefon nömrəsi düzgün deyil.");
        }

        // A listing may be filed under the seller's own storefront, and only while that storefront
        // is active. SellerType is derived from the outcome — it is never accepted from a client.
        Guid? storeId = null;

        if (request.UseStore)
        {
            storeId = await db.Stores.AsNoTracking()
                .Where(s => s.OwnerUserId == userId && s.Status == StoreStatus.Active)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (storeId is null)
            {
                return Result<ListingDetailDto>.Invalid(
                    "useStore", "Aktiv mağazanız olmadığı üçün elanı mağazaya bağlamaq mümkün deyil.");
            }
        }

        var now = clock.UtcNow;
        var title = request.Title.Trim();

        var listing = new Listing
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            StoreId = storeId,
            CategoryId = schema.Category.Id,
            RegionId = region.Value,
            Title = title,
            Slug = ListingMapper.BuildSlug(title),
            Description = request.Description.Trim(),
            Price = request.Price,
            Currency = "AZN",
            Condition = ParseCondition(request.Condition),
            HasDelivery = request.HasDelivery,
            Brand = string.IsNullOrWhiteSpace(request.Brand) ? null : request.Brand.Trim(),
            SellerType = storeId is null ? SellerType.Individual : SellerType.Store,
            ContactPhone = phone,
            ShowPhone = request.ShowPhone,
            Status = ListingStatus.Draft,
            Attributes = attributes.Canonical,
            SearchKey = ListingMapper.BuildSearchKey(title, request.Brand),
            CreatedAt = now
        };

        db.Listings.Add(listing);
        await db.SaveChangesAsync(cancellationToken);

        return await LoadDetailAsync(listing.Id, schema, cancellationToken);
    }

    public async Task<Result<ListingDetailDto>> UpdateAsync(
        Guid id, UpdateListingRequest request, CancellationToken cancellationToken = default)
    {
        var found = await LoadOwnedAsync(id, cancellationToken);

        if (!found.Succeeded)
        {
            return Result<ListingDetailDto>.From(found);
        }

        var listing = found.Value!;

        if (!ListingStateMachine.CanEdit(listing.Status))
        {
            return Result<ListingDetailDto>.Conflict(
                $"Bu statusda ({StatusLabel(listing.Status)}) elana düzəliş etmək mümkün deyil.");
        }

        var allowed = ListingStateMachine.EditableFields(listing.Status);
        var schema = (await categories.GetSchemaAsync(listing.Category.Slug, cancellationToken)).Value;

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var changed = ListingEditableFields.None;

        var description = request.Description.Trim();

        if (!string.Equals(description, listing.Description, StringComparison.Ordinal))
        {
            changed |= ListingEditableFields.Description;
        }

        if (request.Price != listing.Price)
        {
            changed |= ListingEditableFields.Price;
        }

        if (request.HasDelivery != listing.HasDelivery)
        {
            changed |= ListingEditableFields.Delivery;
        }

        if (request.ShowPhone != listing.ShowPhone)
        {
            changed |= ListingEditableFields.PhoneVisibility;
        }

        var title = request.Title.Trim();

        if (!string.Equals(title, listing.Title, StringComparison.Ordinal))
        {
            changed |= ListingEditableFields.Title;
            Reject(errors, allowed, ListingEditableFields.Title, "title", "Dərc olunmuş elanda başlıq dəyişdirilə bilməz.");
        }

        var condition = ParseCondition(request.Condition);

        if (condition != listing.Condition)
        {
            changed |= ListingEditableFields.Condition;
            Reject(errors, allowed, ListingEditableFields.Condition, "condition", "Dərc olunmuş elanda vəziyyət dəyişdirilə bilməz.");
        }

        var brand = string.IsNullOrWhiteSpace(request.Brand) ? null : request.Brand.Trim();

        if (!string.Equals(brand, listing.Brand, StringComparison.Ordinal))
        {
            changed |= ListingEditableFields.Brand;
            Reject(errors, allowed, ListingEditableFields.Brand, "brand", "Dərc olunmuş elanda marka dəyişdirilə bilməz.");
        }

        var phone = PhoneNumber.Normalize(request.ContactPhone);

        if (phone is null)
        {
            errors["contactPhone"] = ["Telefon nömrəsi düzgün deyil."];
        }
        else if (!string.Equals(phone, listing.ContactPhone, StringComparison.Ordinal))
        {
            changed |= ListingEditableFields.ContactPhone;
            Reject(errors, allowed, ListingEditableFields.ContactPhone, "contactPhone", "Dərc olunmuş elanda əlaqə nömrəsi dəyişdirilə bilməz.");
        }

        var regionResult = await ResolveRegionAsync(request.RegionSlug, cancellationToken);

        if (!regionResult.Succeeded)
        {
            errors["regionSlug"] = [regionResult.Message ?? "Region düzgün deyil."];
        }
        else if (regionResult.Value != listing.RegionId)
        {
            changed |= ListingEditableFields.Region;
            Reject(errors, allowed, ListingEditableFields.Region, "regionSlug", "Dərc olunmuş elanda yer dəyişdirilə bilməz.");
        }

        var attributes = attributeValidator.Validate(schema?.Attributes ?? [], request.Attributes);

        if (!attributes.IsValid)
        {
            foreach (var error in attributes.Errors)
            {
                errors[error.Key] = error.Value;
            }
        }
        else if (!SameAttributes(listing.Attributes, attributes.Canonical))
        {
            changed |= ListingEditableFields.Attributes;
            Reject(errors, allowed, ListingEditableFields.Attributes, "attributes", "Dərc olunmuş elanda xüsusiyyətlər dəyişdirilə bilməz.");
        }

        if (errors.Count > 0)
        {
            return Result<ListingDetailDto>.Invalid(errors);
        }

        listing.Description = description;
        listing.Price = request.Price;
        listing.HasDelivery = request.HasDelivery;
        listing.ShowPhone = request.ShowPhone;

        // The wider set only unlocks while the listing has never been live.
        if (allowed.HasFlag(ListingEditableFields.Title))
        {
            listing.Title = title;
            listing.Slug = ListingMapper.BuildSlug(title);
            listing.Condition = condition;
            listing.Brand = brand;
            listing.ContactPhone = phone!;
            listing.RegionId = regionResult.Value;
            listing.Attributes = attributes.Canonical;
            listing.SearchKey = ListingMapper.BuildSearchKey(title, brand);
        }

        // A change buyers can see sends a live listing back through moderation; a phone toggle does not.
        if (ListingStateMachine.RequiresRemoderation(listing.Status, changed))
        {
            listing.Status = ListingStatus.PendingModeration;
        }

        var saved = await SaveAsync(cancellationToken);

        if (!saved.Succeeded)
        {
            return Result<ListingDetailDto>.From(saved);
        }

        return await LoadDetailAsync(listing.Id, schema, cancellationToken);
    }

    public async Task<Result<ListingDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var found = await LoadOwnedAsync(id, cancellationToken, tracking: false);

        if (!found.Succeeded)
        {
            return Result<ListingDetailDto>.From(found);
        }

        var listing = found.Value!;
        var schema = (await categories.GetSchemaAsync(listing.Category.Slug, cancellationToken)).Value;

        return Result<ListingDetailDto>.Success(ListingMapper.ToDetail(listing, schema, storage, clock.UtcNow));
    }

    public async Task<Result<PagedResult<ListingSummaryDto>>> GetMineAsync(
        string? bucket, PageRequest page, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<PagedResult<ListingSummaryDto>>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var query = db.Listings.AsNoTracking().Where(l => l.UserId == userId);

        if (ParseBucket(bucket) is { } status)
        {
            query = query.Where(l => l.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Include(l => l.Category)
            .Include(l => l.Region)
            .Include(l => l.Media)
            // Only the running one: the seller's card says "İrəli çəkilib · until", nothing else.
            .Include(l => l.Promotions.Where(p => p.Status == PromotionStatus.Active))
            .OrderByDescending(l => l.CreatedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        var now = clock.UtcNow;

        return Result<PagedResult<ListingSummaryDto>>.Success(new PagedResult<ListingSummaryDto>(
            items.Select(l => ListingMapper.ToSummary(l, storage, now)).ToList(),
            page.Page,
            page.PageSize,
            total));
    }

    public async Task<Result<ListingPublicDto>> GetPublicAsync(
        long shortId, bool ageConfirmed, CancellationToken cancellationToken = default)
    {
        var listing = await db.Listings.AsNoTracking()
            .Where(l => l.Status == ListingStatus.Active)
            .Include(l => l.Category)
            .Include(l => l.Region)
            .Include(l => l.Media)
            .Include(l => l.User)
            .FirstOrDefaultAsync(l => l.ShortId == shortId, cancellationToken);

        if (listing is null)
        {
            return Result<ListingPublicDto>.NotFound(NotFoundMessage);
        }

        var schema = (await categories.GetSchemaAsync(listing.Category.Slug, cancellationToken)).Value;

        // Restricted and unclassified categories stay visible in browse and search; opening one
        // asks the visitor to confirm their age first. Unclassified is handled the same way as a
        // precaution and is never described as a legal restriction.
        if (schema?.Category.RequiresAgeConfirmation == true && !ageConfirmed)
        {
            return Result<ListingPublicDto>.Invalid(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["ageConfirmation"] = [listing.Category.NameAz]
                },
                "Bu kateqoriyadakı elanı açmaq üçün yaş təsdiqi tələb olunur.");
        }

        viewCounts.Record(listing.Id);

        var isFavorited = currentUser.UserId is { } viewerId
            && await db.Favorites.AsNoTracking()
                .AnyAsync(f => f.UserId == viewerId && f.ListingId == listing.Id, cancellationToken);

        // Only an active storefront produces a store block. A suspended one leaves the listing
        // publicly readable but offers no navigation claiming the store is open.
        var store = listing.StoreId is null
            ? null
            : await db.Stores.AsNoTracking()
                .Where(s => s.Id == listing.StoreId && s.Status == StoreStatus.Active)
                .Select(s => new ListingStoreDto(s.Slug, s.Name, s.IsVerified, s.LogoStorageKey))
                .FirstOrDefaultAsync(cancellationToken);

        if (store is not null && store.LogoUrl is not null)
        {
            store = store with { LogoUrl = storage.GetPublicUrl(store.LogoUrl) };
        }

        return Result<ListingPublicDto>.Success(
            ListingMapper.ToPublic(listing, schema, storage, isFavorited, store));
    }

    public async Task<Result<ListingPhoneDto>> GetPublicPhoneAsync(long shortId, CancellationToken cancellationToken = default)
    {
        var listing = await db.Listings.AsNoTracking()
            .Where(l => l.ShortId == shortId && l.Status == ListingStatus.Active)
            .Select(l => new { l.ContactPhone, l.ShowPhone })
            .FirstOrDefaultAsync(cancellationToken);

        if (listing is null)
        {
            return Result<ListingPhoneDto>.NotFound(NotFoundMessage);
        }

        return listing.ShowPhone
            ? Result<ListingPhoneDto>.Success(new ListingPhoneDto(listing.ContactPhone))
            : Result<ListingPhoneDto>.Forbidden("Satıcı nömrəsini gizlədib.");
    }

    public async Task<Result<ListingDetailDto>> MarkSoldAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var found = await LoadOwnedAsync(id, cancellationToken);

        if (!found.Succeeded)
        {
            return Result<ListingDetailDto>.From(found);
        }

        var listing = found.Value!;

        if (!ListingStateMachine.CanMarkSold(listing.Status))
        {
            return Result<ListingDetailDto>.Conflict("Yalnız saytda olan və ya müddəti bitmiş elan satıldı kimi işarələnə bilər.");
        }

        listing.Status = ListingStatus.Sold;

        var saved = await SaveAsync(cancellationToken);

        return saved.Succeeded
            ? await LoadDetailAsync(listing.Id, null, cancellationToken)
            : Result<ListingDetailDto>.From(saved);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var found = await LoadOwnedAsync(id, cancellationToken);

        if (!found.Succeeded)
        {
            return found;
        }

        var listing = found.Value!;
        var now = clock.UtcNow;

        if (ListingStateMachine.IsHardDeletable(listing.Status))
        {
            // A draft has never been on the site, so there is nothing to keep restorable.
            listing.DeletedAt = now;
        }
        else if (ListingStateMachine.CanRetire(listing.Status))
        {
            // Tap.az behaviour: removal retires the listing and it stays restorable for 30 days.
            listing.Status = ListingStatus.Expired;
            listing.ExpiresAt = now;
        }
        else
        {
            return Result.Conflict("Bu elan silinə bilməz.");
        }

        return await SaveAsync(cancellationToken);
    }

    /// <summary>
    /// Loads a listing the caller may touch. Ownership is checked on the row, so guessing an id is
    /// not enough; moderators and admins may read any listing.
    /// </summary>
    private async Task<Result<Listing>> LoadOwnedAsync(Guid id, CancellationToken cancellationToken, bool tracking = true)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<Listing>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var query = db.Listings
            .Include(l => l.Category)
            .Include(l => l.Region)
            .Include(l => l.Media)
            .Include(l => l.User)
            .AsQueryable();

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        var listing = await query.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

        if (listing is null)
        {
            return Result<Listing>.NotFound(NotFoundMessage);
        }

        var isStaff = currentUser.IsInRole("Admin") || currentUser.IsInRole("Moderator");

        if (listing.UserId != userId && !isStaff)
        {
            // Same answer as a miss, so an id cannot be probed for existence.
            return Result<Listing>.NotFound(NotFoundMessage);
        }

        return Result<Listing>.Success(listing);
    }

    private async Task<Result<ListingDetailDto>> LoadDetailAsync(
        Guid id, CategorySchemaDto? schema, CancellationToken cancellationToken)
    {
        var listing = await db.Listings.AsNoTracking()
            .Include(l => l.Category)
            .Include(l => l.Region)
            .Include(l => l.Media)
            .FirstAsync(l => l.Id == id, cancellationToken);

        schema ??= (await categories.GetSchemaAsync(listing.Category.Slug, cancellationToken)).Value;

        return Result<ListingDetailDto>.Success(ListingMapper.ToDetail(listing, schema, storage, clock.UtcNow));
    }

    private async Task<Result<int>> ResolveRegionAsync(string regionSlug, CancellationToken cancellationToken)
    {
        var regionId = await db.Regions.AsNoTracking()
            .Where(r => r.Slug == regionSlug)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (regionId is null)
        {
            return Result<int>.Invalid("regionSlug", "Region tapılmadı.");
        }

        // Phase 3 rule, enforced server-side: only an active, selectable place can carry a listing.
        var valid = await regions.ValidateListingRegionAsync(regionId.Value, cancellationToken);

        return valid.Succeeded
            ? Result<int>.Success(regionId.Value)
            : Result<int>.Invalid("regionSlug", valid.Message ?? "Region düzgün deyil.");
    }

    private async Task<Result> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Conflict("Elan başqa yerdə dəyişdirilib. Səhifəni yeniləyib yenidən cəhd edin.");
        }
    }

    private static void Reject(
        Dictionary<string, string[]> errors,
        ListingEditableFields allowed,
        ListingEditableFields field,
        string key,
        string message)
    {
        if (!allowed.HasFlag(field))
        {
            errors[key] = [message];
        }
    }

    private static bool SameAttributes(
        IReadOnlyDictionary<string, JsonElement> current,
        IReadOnlyDictionary<string, JsonElement> candidate)
    {
        if (current.Count != candidate.Count)
        {
            return false;
        }

        foreach (var (key, value) in candidate)
        {
            if (!current.TryGetValue(key, out var existing)
                || !string.Equals(existing.GetRawText(), value.GetRawText(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static ListingCondition ParseCondition(string? value) =>
        Enum.TryParse<ListingCondition>(value, ignoreCase: true, out var parsed) ? parsed : ListingCondition.Used;

    internal static ListingStatus? ParseBucket(string? bucket) => bucket?.ToLowerInvariant() switch
    {
        "draft" => ListingStatus.Draft,
        "pending" => ListingStatus.PendingModeration,
        "active" => ListingStatus.Active,
        "rejected" => ListingStatus.Rejected,
        "expired" => ListingStatus.Expired,
        "sold" => ListingStatus.Sold,
        "blocked" => ListingStatus.Blocked,
        _ => null
    };

    internal static string StatusLabel(ListingStatus status) => status switch
    {
        ListingStatus.Draft => "Qaralama",
        ListingStatus.PendingModeration => "Gözləmədə",
        ListingStatus.Active => "Hazırda saytda",
        ListingStatus.Rejected => "Dərc olunmamış",
        ListingStatus.Expired => "Müddəti başa çatmış",
        ListingStatus.Sold => "Satılıb",
        ListingStatus.Blocked => "Bloklanıb",
        _ => status.ToString()
    };
}
