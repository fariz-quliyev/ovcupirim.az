using System.Globalization;
using System.Text.Json;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Categories;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Payments;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Listings;

/// <summary>
/// Turns a listing entity into the shapes the API returns. Attribute values are rendered here,
/// against the category schema, so the frontend never has to know what a key means.
/// </summary>
public static class ListingMapper
{
    public static string BuildSlug(string title) => AzerbaijaniText.ToSlug(title, 120);

    public static string? BuildSearchKey(string title, string? brand)
    {
        var normalized = AzerbaijaniText.Normalize($"{title} {brand}".Trim());
        return normalized.Length == 0 ? null : normalized[..Math.Min(normalized.Length, 200)];
    }

    public static string CanonicalPath(Listing listing) => $"/elan/{listing.Slug}-{listing.ShortId}";

    public static IReadOnlyList<ListingMediaDto> Media(Listing listing, IFileStorage storage) =>
        listing.Media
            .OrderBy(m => m.SortOrder)
            .Select(m => new ListingMediaDto(
                m.Id,
                storage.GetPublicUrl(m.StorageKey),
                m.Variants.ToDictionary(v => v.Key, v => storage.GetPublicUrl(v.Value)),
                m.Width,
                m.Height,
                m.SizeBytes,
                m.SortOrder,
                m.IsPrimary))
            .ToList();

    private static string? PrimaryImageUrl(Listing listing, IFileStorage storage)
    {
        var primary = listing.Media.FirstOrDefault(m => m.IsPrimary) ?? listing.Media.MinBy(m => m.SortOrder);

        if (primary is null)
        {
            return null;
        }

        return primary.Variants.TryGetValue("card", out var card)
            ? storage.GetPublicUrl(card)
            : storage.GetPublicUrl(primary.StorageKey);
    }

    /// <summary>
    /// Label/value rows for the detail page. Ordered by the schema, not by the JSON bag, so the
    /// table reads the same way for every listing in a category.
    /// </summary>
    public static IReadOnlyList<ListingAttributeDto> DisplayAttributes(
        IReadOnlyDictionary<string, JsonElement> attributes,
        CategorySchemaDto? schema)
    {
        if (schema is null)
        {
            return [];
        }

        var rows = new List<ListingAttributeDto>();

        foreach (var field in schema.Attributes.OrderBy(a => a.SortOrder))
        {
            if (!attributes.TryGetValue(field.Key, out var value))
            {
                continue;
            }

            var display = Render(field, value);

            if (!string.IsNullOrWhiteSpace(display))
            {
                rows.Add(new ListingAttributeDto(field.Key, field.LabelAz, display));
            }
        }

        return rows;
    }

    private static string Render(AttributeSchemaDto field, JsonElement value)
    {
        var dataType = Enum.Parse<AttributeDataType>(field.DataType);

        return dataType switch
        {
            AttributeDataType.Boolean => value.ValueKind == JsonValueKind.True ? "Bəli" : "Xeyr",
            AttributeDataType.Number => WithUnit(value.GetDecimal().ToString("0.##", CultureInfo.InvariantCulture), field.Unit),
            AttributeDataType.Select => OptionLabel(field, value.GetString()),
            AttributeDataType.MultiSelect => string.Join(
                ", ",
                value.EnumerateArray().Select(v => OptionLabel(field, v.GetString()))),
            _ => WithUnit(value.GetString() ?? string.Empty, field.Unit)
        };
    }

    private static string WithUnit(string value, string? unit) =>
        string.IsNullOrWhiteSpace(unit) ? value : $"{value} {unit}";

    private static string OptionLabel(AttributeSchemaDto field, string? optionValue) =>
        field.Options?.FirstOrDefault(o => o.Value == optionValue)?.LabelAz ?? optionValue ?? string.Empty;

    public static ListingDetailDto ToDetail(
        Listing listing,
        CategorySchemaDto? schema,
        IFileStorage storage,
        DateTimeOffset now) =>
        new(
            listing.Id,
            listing.ShortId,
            listing.Slug,
            listing.Status.ToString(),
            listing.RejectionReason,
            listing.CategoryId,
            listing.Category.Slug,
            listing.Category.NameAz,
            schema?.Category.Path ?? [],
            listing.RegionId,
            listing.Region.Slug,
            listing.Region.NameAz,
            listing.Title,
            listing.Description,
            listing.Price,
            listing.Currency,
            listing.Condition.ToString(),
            listing.HasDelivery,
            listing.Brand,
            listing.SellerType.ToString(),
            listing.ContactPhone,
            listing.ShowPhone,
            listing.Attributes,
            DisplayAttributes(listing.Attributes, schema),
            Media(listing, storage),
            listing.PublishedAt,
            listing.ExpiresAt,
            ListingStateMachine.RestorableUntil(listing.Status, listing.ExpiresAt),
            listing.ViewCount,
            listing.AgeConfirmedAt is not null,
            listing.CreatedAt,
            listing.UpdatedAt,
            ListingStateMachine.Capabilities(listing.Status, listing.ExpiresAt, now));

    public static ListingSummaryDto ToSummary(Listing listing, IFileStorage storage, DateTimeOffset now) =>
        new(
            listing.Id,
            listing.ShortId,
            listing.Slug,
            listing.Status.ToString(),
            listing.RejectionReason,
            listing.Title,
            listing.Price,
            listing.Currency,
            listing.Region.NameAz,
            listing.Category.NameAz,
            PrimaryImageUrl(listing, storage),
            listing.Media.Count,
            listing.PublishedAt,
            listing.ExpiresAt,
            ListingStateMachine.RestorableUntil(listing.Status, listing.ExpiresAt),
            listing.ViewCount,
            listing.CreatedAt,
            ListingStateMachine.Capabilities(listing.Status, listing.ExpiresAt, now),
            ActivePromotion(listing));

    /// <summary>
    /// The one active promotion, when the caller loaded <c>Promotions</c> (the seller's own list
    /// does; nothing else needs to). One-at-a-time is enforced by the database, so "first" is "the".
    /// </summary>
    private static ListingPromotionDto? ActivePromotion(Listing listing) =>
        listing.Promotions.FirstOrDefault(p => p.Status == PromotionStatus.Active) is { } active
            ? new ListingPromotionDto(
                active.Status.ToString(), active.ActivatedAt, active.ExpiresAt, PromotionStateMachine.BumpIntervalHours)
            : null;

    public static ListingPublicDto ToPublic(
        Listing listing,
        CategorySchemaDto? schema,
        IFileStorage storage,
        bool isFavorited = false,
        ListingStoreDto? store = null) =>
        new(
            listing.ShortId,
            listing.Slug,
            CanonicalPath(listing),
            listing.Title,
            listing.Description,
            listing.Price,
            listing.Currency,
            listing.Condition.ToString(),
            listing.HasDelivery,
            listing.Brand,
            listing.SellerType.ToString(),
            listing.CategoryId,
            listing.Category.Slug,
            listing.Category.NameAz,
            schema?.Category.Path ?? [],
            listing.Region.Slug,
            listing.Region.NameAz,
            DisplayAttributes(listing.Attributes, schema),
            Media(listing, storage),
            // A listing presented as a storefront's is the store's, not the person's: the public
            // face is the shop, and the owner's own name is withheld. When there is no store block
            // — an individual sale, or the D-5 fallback for a storefront that is no longer active —
            // the seller name is what the page has to show, so it is sent as before.
            store is null ? listing.User.FullName : null,
            store,
            listing.ShowPhone,
            listing.ShowPhone ? PhoneNumber.Mask(listing.ContactPhone) : null,
            isFavorited,
            listing.PublishedAt ?? listing.CreatedAt,
            listing.ViewCount);
}
