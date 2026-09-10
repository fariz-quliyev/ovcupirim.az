using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Categories;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Listings.Search;

/// <summary>Raw query string, exactly as the catalogue page sends it.</summary>
public sealed record ListingSearchRequest
{
    public string? Q { get; init; }
    public string? Category { get; init; }
    public string? Region { get; init; }

    /// <summary>
    /// Resolved by the caller, not parsed from the query string: only the store page sets it, and
    /// it has already confirmed the storefront is public.
    /// </summary>
    public Guid? StoreId { get; init; }
    public decimal? PriceMin { get; init; }
    public decimal? PriceMax { get; init; }
    public string? Condition { get; init; }
    public bool? Delivery { get; init; }
    public string? SellerType { get; init; }
    public string? Sort { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    /// <summary>Everything sent under the <c>attr.</c> prefix, keys already stripped of it.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public interface IListingQueryParser
{
    Task<Result<ListingQuery>> ParseAsync(ListingSearchRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns query-string text into a validated query.
/// </summary>
/// <remarks>
/// This is the whitelist that makes the raw SQL safe: an attribute key survives only if the chosen
/// category's schema declares it and marks it filterable, and every value is coerced to the
/// canonical JSON type before it is used. An unrecognised filter is dropped rather than rejected,
/// so a stale bookmark still returns results instead of an error page.
/// </remarks>
public sealed class ListingQueryParser(IAppDbContext db, ICategoryService categories) : IListingQueryParser
{
    /// <summary>Deep paging is capped: nobody browses to page 500, but a crawler will try.</summary>
    public const int MaxPage = 100;

    public async Task<Result<ListingQuery>> ParseAsync(
        ListingSearchRequest request, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var page = Math.Clamp(request.Page < 1 ? 1 : request.Page, 1, MaxPage);
        var pageSize = request.PageSize switch
        {
            < 1 => PageRequest.DefaultPageSize,
            > PageRequest.MaxPageSize => PageRequest.MaxPageSize,
            _ => request.PageSize
        };

        var categoryIds = Array.Empty<int>();
        CategorySchemaDto? schema = null;

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            // The category plus every active descendant, so browsing a parent shows its children.
            // Shared with the store directory, so both mean the same thing by "this category".
            var resolved = await categories.GetSubtreeIdsAsync(request.Category, cancellationToken);

            if (resolved is null)
            {
                errors["category"] = ["Kateqoriya tapılmadı."];
            }
            else
            {
                categoryIds = resolved;
                schema = (await categories.GetSchemaAsync(request.Category, cancellationToken)).Value;
            }
        }

        int? regionId = null;

        if (!string.IsNullOrWhiteSpace(request.Region))
        {
            // Only a place a seller could have chosen: the same active-and-selectable rule Phase 3
            // enforces everywhere else.
            regionId = await db.Regions.AsNoTracking()
                .Where(r => r.Slug == request.Region && r.IsActive && r.IsSelectable)
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (regionId is null)
            {
                errors["region"] = ["Region tapılmadı."];
            }
        }

        if (request.PriceMin is { } min && min < 0)
        {
            errors["priceMin"] = ["Qiymət mənfi ola bilməz."];
        }

        if (request.PriceMax is { } max && max < 0)
        {
            errors["priceMax"] = ["Qiymət mənfi ola bilməz."];
        }

        if (request.PriceMin is { } lo && request.PriceMax is { } hi && lo > hi)
        {
            errors["priceMin"] = ["Minimum qiymət maksimumdan böyük ola bilməz."];
        }

        if (errors.Count > 0)
        {
            return Result<ListingQuery>.Invalid(errors);
        }

        var folded = AzerbaijaniText.Normalize(request.Q);
        var hasText = folded.Length > 0;

        return Result<ListingQuery>.Success(new ListingQuery
        {
            Text = hasText ? folded : null,
            RawText = hasText ? request.Q!.Trim() : null,
            CategoryIds = categoryIds,
            RegionId = regionId,
            StoreId = request.StoreId,
            PriceMin = request.PriceMin,
            PriceMax = request.PriceMax,
            Condition = ParseEnum<ListingCondition>(request.Condition),
            HasDelivery = request.Delivery,
            SellerType = ParseEnum<SellerType>(request.SellerType),
            // Attribute filters exist only inside a category, because only a category has a schema
            // to validate them against. Outside one they are dropped, exactly as Tap.az does.
            Attributes = schema is null ? [] : BuildAttributeFilters(schema, request.Attributes),
            Sort = ParseSort(request.Sort, hasText),
            Page = page,
            PageSize = pageSize
        });
    }

    private static List<AttributeFilter> BuildAttributeFilters(
        CategorySchemaDto schema, IReadOnlyDictionary<string, string> submitted)
    {
        var filters = new List<AttributeFilter>();
        var filterable = schema.Attributes
            .Where(a => a.IsFilterable)
            .ToDictionary(a => a.Key, StringComparer.Ordinal);

        foreach (var field in filterable.Values.OrderBy(a => a.SortOrder))
        {
            var dataType = Enum.Parse<AttributeDataType>(field.DataType);

            if (dataType == AttributeDataType.Number)
            {
                var min = ParseDecimal(Value(submitted, $"{field.Key}_min"));
                var max = ParseDecimal(Value(submitted, $"{field.Key}_max"));

                if (min is not null || max is not null)
                {
                    filters.Add(new AttributeFilter.Range(field.Key, min, max));
                }

                continue;
            }

            var raw = Value(submitted, field.Key);

            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            switch (dataType)
            {
                case AttributeDataType.Boolean:
                    if (bool.TryParse(raw, out var flag))
                    {
                        filters.Add(new AttributeFilter.Flag(field.Key, flag));
                    }

                    break;

                case AttributeDataType.Select:
                case AttributeDataType.MultiSelect:
                    var allowed = field.Options?.Select(o => o.Value).ToHashSet(StringComparer.Ordinal) ?? [];

                    // Unknown option values are discarded, so a renamed option degrades to a
                    // broader result set rather than an error.
                    var chosen = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(allowed.Contains)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();

                    if (chosen.Count > 0)
                    {
                        filters.Add(new AttributeFilter.Containment(
                            field.Key,
                            [.. chosen.Select(value => ContainmentJson(field.Key, value, dataType))]));
                    }

                    break;
            }
        }

        return filters;
    }

    /// <summary>
    /// The canonical shape the value was stored in: a bare string for Select, a one-element array
    /// for MultiSelect, which containment treats as "contains this member".
    /// </summary>
    private static string ContainmentJson(string key, string value, AttributeDataType dataType)
    {
        var payload = dataType == AttributeDataType.MultiSelect
            ? (object)new[] { value }
            : value;

        return JsonSerializer.Serialize(new Dictionary<string, object> { [key] = payload });
    }

    private static string? Value(IReadOnlyDictionary<string, string> submitted, string key) =>
        submitted.TryGetValue(key, out var value) ? value : null;

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Accepts a comma decimal separator, matching what the attribute validator accepts on input.
        var normalized = raw.Trim().Replace(',', '.');

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static T? ParseEnum<T>(string? raw) where T : struct, Enum =>
        Enum.TryParse<T>(raw, ignoreCase: true, out var value) ? value : null;

    private static ListingSort ParseSort(string? raw, bool hasText)
    {
        var sort = raw?.ToLowerInvariant() switch
        {
            "price_asc" => ListingSort.PriceAscending,
            "price_desc" => ListingSort.PriceDescending,
            "relevance" => ListingSort.Relevance,
            _ => ListingSort.Newest
        };

        // Relevance is meaningless without a term to rank against.
        return sort == ListingSort.Relevance && !hasText ? ListingSort.Newest : sort;
    }
}
