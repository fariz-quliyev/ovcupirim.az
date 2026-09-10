using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Regions;

/// <summary>
/// A location a user can pick. Deliberately flat and free of administrative detail: the picker
/// offers one practical place, not a walk down the territorial hierarchy.
/// </summary>
public sealed record RegionDto(
    int Id,
    string Slug,
    string NameAz,
    string? NameRu,
    int ListingCount);

/// <summary>The administrative view, for administration and verifying an authoritative import.</summary>
public sealed record AdminRegionDto(
    int Id,
    int? ParentId,
    string Slug,
    string NameAz,
    string? NameRu,
    string Type,
    int Depth,
    bool IsActive,
    bool IsSelectable,
    int SortOrder);

public sealed record RegionStatDto(int Id, string Slug, string NameAz, int ListingCount);

/// <summary>
/// One row of an authoritative import. <see cref="IsSelectable"/> is nullable on purpose: a missing
/// value is rejected rather than defaulted, so an internal administrative record can never become
/// user-selectable by omission.
/// </summary>
/// <summary>
/// One row of the authoritative dataset.
/// </summary>
/// <remarks>
/// The optional descriptive fields are <see cref="Omittable{T}"/> rather than plain nullables so a
/// partial import cannot erase what it never mentioned: omitting <c>nameRu</c> or a coordinate
/// leaves the stored value alone, while sending an explicit <c>null</c> clears it. Required
/// decisions — the name, and <c>isSelectable</c> — stay plain, because they must always be stated.
/// </remarks>
public sealed record RegionImportRow(
    string NameAz,
    Omittable<string> NameRu,
    string? Slug,
    string? Type,
    string? ParentSlug,
    bool? IsSelectable,
    Omittable<double?> Latitude,
    Omittable<double?> Longitude,
    int? SortOrder);

public sealed record RegionImportResult(int Inserted, int Updated, int Skipped, IReadOnlyList<string> Errors);

public interface IRegionService
{
    /// <summary>Active, user-selectable locations, flat and server-ordered.</summary>
    Task<IReadOnlyList<RegionDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RegionStatDto>> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>The complete administrative dataset, including records users never see.</summary>
    Task<IReadOnlyList<AdminRegionDto>> GetAllForAdminAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-loads the authoritative dataset. Idempotent: an existing slug is updated rather than
    /// duplicated. Coordinates are only ever taken from the supplied rows, never generated.
    /// </summary>
    Task<Result<RegionImportResult>> ImportAsync(IReadOnlyList<RegionImportRow> rows, CancellationToken cancellationToken = default);

    /// <summary>
    /// Server-side guard for a listing's location: the region must exist, be active and be
    /// selectable. Phase 4 calls this before persisting; the frontend never decides this.
    /// </summary>
    Task<Result> ValidateListingRegionAsync(int regionId, CancellationToken cancellationToken = default);
}

public sealed class RegionService(IAppDbContext db, ITaxonomyCache cache, IDateTimeProvider clock) : IRegionService
{
    public async Task<IReadOnlyList<RegionDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await cache.GetOrCreateAsync(
            "regions:selectable",
            async token =>
            {
                var regions = await SelectableRegions()
                    .Select(r => new RegionDto(r.Id, r.Slug, r.NameAz, r.NameRu, r.ListingCount))
                    .ToListAsync(token);

                return (IReadOnlyList<RegionDto>)regions;
            },
            cancellationToken);

    public async Task<IReadOnlyList<RegionStatDto>> GetStatsAsync(CancellationToken cancellationToken = default) =>
        await SelectableRegions()
            .Select(r => new RegionStatDto(r.Id, r.Slug, r.NameAz, r.ListingCount))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AdminRegionDto>> GetAllForAdminAsync(CancellationToken cancellationToken = default) =>
        await db.Regions.AsNoTracking()
            .OrderBy(r => r.Depth).ThenBy(r => r.SortOrder).ThenBy(r => r.NameAz)
            .Select(r => new AdminRegionDto(
                r.Id,
                r.ParentId,
                r.Slug,
                r.NameAz,
                r.NameRu,
                r.Type.ToString(),
                r.Depth,
                r.IsActive,
                r.IsSelectable,
                r.SortOrder))
            .ToListAsync(cancellationToken);

    public async Task<Result> ValidateListingRegionAsync(int regionId, CancellationToken cancellationToken = default)
    {
        var region = await db.Regions.AsNoTracking()
            .Where(r => r.Id == regionId)
            .Select(r => new { r.IsActive, r.IsSelectable })
            .FirstOrDefaultAsync(cancellationToken);

        if (region is null)
        {
            return Result.Failure(ResultError.Validation, "Region tapılmadı.");
        }

        // An internal administrative record is not a valid listing location, even though it exists.
        if (!region.IsActive || !region.IsSelectable)
        {
            return Result.Failure(ResultError.Validation, "Bu region seçilə bilməz.");
        }

        return Result.Success();
    }

    public async Task<Result<RegionImportResult>> ImportAsync(
        IReadOnlyList<RegionImportRow> rows,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
        {
            return Result<RegionImportResult>.Failure(ResultError.Validation, "İdxal üçün məlumat yoxdur.");
        }

        var existing = await db.Regions.ToDictionaryAsync(r => r.Slug, cancellationToken);
        var errors = new List<string>();
        int inserted = 0, updated = 0, skipped = 0;

        // Parents first, so a child can resolve its parent within the same import.
        foreach (var row in rows.OrderBy(r => string.IsNullOrWhiteSpace(r.ParentSlug) ? 0 : 1))
        {
            if (string.IsNullOrWhiteSpace(row.NameAz))
            {
                errors.Add("Ad boş ola bilməz.");
                skipped++;
                continue;
            }

            // Selectability is a product decision and must be stated, never inferred.
            if (row.IsSelectable is not { } isSelectable)
            {
                errors.Add($"'{row.NameAz}' üçün isSelectable göstərilməyib. Bu sahə mütləqdir.");
                skipped++;
                continue;
            }

            var slug = string.IsNullOrWhiteSpace(row.Slug) ? AzerbaijaniText.ToSlug(row.NameAz) : row.Slug.Trim();

            if (!AzerbaijaniText.IsSlug(slug))
            {
                errors.Add($"'{row.NameAz}' üçün slug düzgün deyil: '{slug}'.");
                skipped++;
                continue;
            }

            if (!TryParseType(row.Type, out var type))
            {
                errors.Add($"'{row.NameAz}' üçün naməlum region tipi: '{row.Type}'.");
                skipped++;
                continue;
            }

            Region? parent = null;

            if (!string.IsNullOrWhiteSpace(row.ParentSlug))
            {
                if (!existing.TryGetValue(row.ParentSlug.Trim(), out parent))
                {
                    errors.Add($"'{row.NameAz}' üçün valideyn region tapılmadı: '{row.ParentSlug}'.");
                    skipped++;
                    continue;
                }

                if (parent.Depth >= 1)
                {
                    errors.Add($"'{row.NameAz}': region iyerarxiyası iki səviyyə ilə məhdudlaşır.");
                    skipped++;
                    continue;
                }
            }

            // Audit M-5: making a place unselectable while sellers still have live listings there
            // would strand them — visible in the unfiltered catalogue, unreachable by the region
            // filter and missing from the facet counts. Rather than silently choosing to hide,
            // move or expire those listings, the row is refused the same way a missing
            // isSelectable is, and the operator is told which region and how many listings.
            if (existing.TryGetValue(slug, out var candidate)
                && candidate.IsSelectable
                && !isSelectable)
            {
                var liveListings = await db.Listings
                    .AsNoTracking()
                    .CountAsync(l => l.RegionId == candidate.Id && l.Status == ListingStatus.Active, cancellationToken);

                if (liveListings > 0)
                {
                    errors.Add(
                        $"'{row.NameAz}' regionunda {liveListings} aktiv elan var. " +
                        "Elanlar başqa regiona köçürülənə və ya müddəti bitənə qədər bu region seçimdən çıxarıla bilməz.");
                    skipped++;
                    continue;
                }
            }

            if (existing.TryGetValue(slug, out var region))
            {
                region.NameAz = row.NameAz.Trim();
                region.NameRu = row.NameRu.Or(region.NameRu);
                region.Type = type;
                region.ParentId = parent?.Id;
                region.Depth = parent is null ? 0 : 1;
                region.IsSelectable = isSelectable;

                // Omitted means "leave it"; an explicit null means "clear it". A dataset that does
                // not carry coordinates must not wipe the ones already loaded.
                region.Latitude = row.Latitude.Or(region.Latitude);
                region.Longitude = row.Longitude.Or(region.Longitude);
                region.SortOrder = row.SortOrder ?? region.SortOrder;
                region.UpdatedAt = clock.UtcNow;
                updated++;
            }
            else
            {
                region = new Region
                {
                    Slug = slug,
                    NameAz = row.NameAz.Trim(),
                    NameRu = row.NameRu.Value,
                    Type = type,
                    ParentId = parent?.Id,
                    Depth = parent is null ? 0 : 1,
                    IsSelectable = isSelectable,
                    Latitude = row.Latitude.Value,
                    Longitude = row.Longitude.Value,
                    SortOrder = row.SortOrder ?? 0,
                    IsActive = true,
                    CreatedAt = clock.UtcNow
                };

                db.Regions.Add(region);
                existing[slug] = region;
                inserted++;
            }

            // Parents must exist before children reference them.
            if (parent is null)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        cache.Invalidate();

        return Result<RegionImportResult>.Success(new RegionImportResult(inserted, updated, skipped, errors));
    }

    /// <summary>Active and user-selectable, in the order the client renders them.</summary>
    private IQueryable<Region> SelectableRegions() =>
        db.Regions.AsNoTracking()
            .Where(r => r.IsActive && r.IsSelectable)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.NameAz);

    private static bool TryParseType(string? value, out RegionType type)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            type = RegionType.Rayon;
            return true;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out type);
    }
}
