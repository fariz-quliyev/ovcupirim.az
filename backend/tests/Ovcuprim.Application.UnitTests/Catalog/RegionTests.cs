using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Regions;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Catalog;

/// <summary>
/// The location model keeps the administrative hierarchy internally while offering users a single
/// flat list of practical places. These tests pin that separation.
/// </summary>
public class RegionTests
{
    private static async Task<(TaxonomyTestHarness Harness, IRegionService Service)> BuildAsync()
    {
        var harness = new TaxonomyTestHarness();
        var service = new RegionService(harness.Db, harness.Cache, harness.Clock);
        return (harness, service);
    }

    private static Region Make(
        string slug, string nameAz, RegionType type = RegionType.Rayon,
        bool isActive = true, bool isSelectable = true, int? parentId = null, int sortOrder = 100) => new()
    {
        Slug = slug,
        NameAz = nameAz,
        Type = type,
        IsActive = isActive,
        IsSelectable = isSelectable,
        ParentId = parentId,
        Depth = parentId is null ? 0 : 1,
        SortOrder = sortOrder
    };

    [Fact]
    public async Task IsSelectable_persists_and_defaults_to_true()
    {
        var (h, _) = await BuildAsync();
        using var _h = h;

        h.Db.Regions.Add(Make("baki", "Bakı", RegionType.City));
        h.Db.Regions.Add(Make("nesimi", "Nəsimi", RegionType.District, isSelectable: false));
        await h.Db.SaveChangesAsync();

        var defaulted = await h.Db.Regions.SingleAsync(r => r.Slug == "baki");
        var internalRecord = await h.Db.Regions.SingleAsync(r => r.Slug == "nesimi");

        Assert.True(defaulted.IsSelectable);
        Assert.False(internalRecord.IsSelectable);
    }

    [Fact]
    public async Task The_public_list_excludes_non_selectable_and_inactive_regions()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        h.Db.Regions.Add(Make("baki", "Bakı", RegionType.City, sortOrder: 10));
        h.Db.Regions.Add(Make("nesimi", "Nəsimi", RegionType.District, isSelectable: false, sortOrder: 20));
        h.Db.Regions.Add(Make("kohne", "Köhnə", isActive: false, sortOrder: 30));
        await h.Db.SaveChangesAsync();

        var slugs = (await service.GetAllAsync()).Select(r => r.Slug).ToArray();

        Assert.Equal(["baki"], slugs);
    }

    [Fact]
    public async Task The_public_list_is_flat_with_no_hierarchy_exposed()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        h.Db.Regions.Add(Make("abseron", "Abşeron", sortOrder: 10));
        await h.Db.SaveChangesAsync();
        var parent = await h.Db.Regions.SingleAsync();

        // A selectable child still appears as a peer, not nested under its parent.
        h.Db.Regions.Add(Make("xirdalan", "Xırdalan", RegionType.City, parentId: parent.Id, sortOrder: 20));
        await h.Db.SaveChangesAsync();

        var list = await service.GetAllAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal(["abseron", "xirdalan"], list.Select(r => r.Slug).ToArray());
    }

    [Fact]
    public async Task The_public_list_is_ordered_by_sort_order_then_name()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        h.Db.Regions.Add(Make("masalli", "Masallı", sortOrder: 50));
        h.Db.Regions.Add(Make("gence", "Gəncə", RegionType.City, sortOrder: 20));
        h.Db.Regions.Add(Make("baki", "Bakı", RegionType.City, sortOrder: 10));
        h.Db.Regions.Add(Make("astara", "Astara", sortOrder: 50));
        await h.Db.SaveChangesAsync();

        var slugs = (await service.GetAllAsync()).Select(r => r.Slug).ToArray();

        // Pinned cities first by SortOrder, then alphabetically inside the same rank.
        Assert.Equal(["baki", "gence", "astara", "masalli"], slugs);
    }

    [Fact]
    public async Task Stats_use_the_same_active_and_selectable_filter()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        h.Db.Regions.Add(Make("baki", "Bakı", RegionType.City));
        h.Db.Regions.Add(Make("nesimi", "Nəsimi", RegionType.District, isSelectable: false));
        h.Db.Regions.Add(Make("kohne", "Köhnə", isActive: false));
        await h.Db.SaveChangesAsync();

        var stats = await service.GetStatsAsync();

        Assert.Equal(["baki"], stats.Select(s => s.Slug).ToArray());
    }

    [Fact]
    public async Task The_admin_view_returns_the_full_hierarchy_including_internal_records()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        h.Db.Regions.Add(Make("abseron", "Abşeron", sortOrder: 10));
        await h.Db.SaveChangesAsync();
        var parent = await h.Db.Regions.SingleAsync();

        h.Db.Regions.Add(Make("nesimi", "Nəsimi", RegionType.District, isSelectable: false, parentId: parent.Id, sortOrder: 20));
        h.Db.Regions.Add(Make("kohne", "Köhnə", isActive: false, sortOrder: 30));
        await h.Db.SaveChangesAsync();

        var all = await service.GetAllForAdminAsync();
        var child = all.Single(r => r.Slug == "nesimi");

        Assert.Equal(3, all.Count);
        Assert.False(child.IsSelectable);
        Assert.Equal(parent.Id, child.ParentId);
        Assert.Equal(1, child.Depth);
        Assert.Equal("District", child.Type);
        Assert.Contains(all, r => r.Slug == "kohne" && !r.IsActive);
    }

    [Fact]
    public async Task Import_requires_an_explicit_isSelectable()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        var result = await service.ImportAsync([
            new("Şirvan", null, null, "City", null, IsSelectable: true, null, null, 10),
            new("Naməlum", null, null, "Rayon", null, IsSelectable: null, null, null, 20)
        ]);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.Inserted);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Contains(result.Value.Errors, e => e.Contains("isSelectable", StringComparison.Ordinal));
        Assert.False(await h.Db.Regions.AnyAsync(r => r.Slug == "namelum"));
    }

    [Fact]
    public async Task Import_stores_the_supplied_selectability_rather_than_guessing()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        await service.ImportAsync([
            new("Abşeron", null, null, "Rayon", null, IsSelectable: true, null, null, 10),
            new("Nəsimi", null, null, "District", "abseron", IsSelectable: false, null, null, 20)
        ]);

        var selectable = await h.Db.Regions.SingleAsync(r => r.Slug == "abseron");
        var internalRecord = await h.Db.Regions.SingleAsync(r => r.Slug == "nesimi");

        Assert.True(selectable.IsSelectable);
        Assert.False(internalRecord.IsSelectable);
        Assert.Equal(selectable.Id, internalRecord.ParentId);
    }

    [Fact]
    public async Task Import_is_idempotent_and_updates_selectability_on_a_second_run()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        RegionImportRow[] rows = [new("Masallı", null, null, "Rayon", null, IsSelectable: true, null, null, 10)];

        var first = await service.ImportAsync(rows);
        var second = await service.ImportAsync(rows);

        Assert.Equal(1, first.Value!.Inserted);
        Assert.Equal(0, second.Value!.Inserted);
        Assert.Equal(1, second.Value.Updated);
        Assert.Equal(1, await h.Db.Regions.CountAsync());

        // A later correction flips the flag rather than inserting a duplicate.
        await service.ImportAsync([new("Masallı", null, null, "Rayon", null, IsSelectable: false, null, null, 10)]);

        Assert.False((await h.Db.Regions.SingleAsync()).IsSelectable);
        Assert.Equal(1, await h.Db.Regions.CountAsync());
    }

    [Fact]
    public async Task Import_never_invents_coordinates()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        await service.ImportAsync([new("Quba", null, null, "Rayon", null, IsSelectable: true, null, null, 10)]);

        var region = await h.Db.Regions.SingleAsync();

        Assert.Null(region.Latitude);
        Assert.Null(region.Longitude);
    }
}

public class ListingRegionGuardTests
{
    /// <summary>
    /// The server-side rule Phase 4 calls before persisting a listing. Frontend validation is a
    /// convenience; this is the enforcement.
    /// </summary>
    private static async Task<(TaxonomyTestHarness Harness, IRegionService Service, int Selectable, int Internal, int Inactive)>
        BuildAsync()
    {
        var harness = new TaxonomyTestHarness();
        var service = new RegionService(harness.Db, harness.Cache, harness.Clock);

        var selectable = new Region { Slug = "baki", NameAz = "Bakı", Type = RegionType.City, IsActive = true, IsSelectable = true };
        var internalRecord = new Region { Slug = "nesimi", NameAz = "Nəsimi", Type = RegionType.District, IsActive = true, IsSelectable = false };
        var inactive = new Region { Slug = "kohne", NameAz = "Köhnə", IsActive = false, IsSelectable = true };

        harness.Db.Regions.AddRange(selectable, internalRecord, inactive);
        await harness.Db.SaveChangesAsync();

        return (harness, service, selectable.Id, internalRecord.Id, inactive.Id);
    }

    [Fact]
    public async Task Accepts_an_active_selectable_region()
    {
        var (h, service, selectable, _, _) = await BuildAsync();
        using var _h = h;

        Assert.True((await service.ValidateListingRegionAsync(selectable)).Succeeded);
    }

    [Fact]
    public async Task Rejects_an_internal_administrative_record()
    {
        var (h, service, _, internalRecord, _) = await BuildAsync();
        using var _h = h;

        var result = await service.ValidateListingRegionAsync(internalRecord);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Validation, result.Error);
    }

    [Fact]
    public async Task Rejects_an_inactive_region()
    {
        var (h, service, _, _, inactive) = await BuildAsync();
        using var _h = h;

        Assert.False((await service.ValidateListingRegionAsync(inactive)).Succeeded);
    }

    [Fact]
    public async Task Rejects_an_unknown_region()
    {
        var (h, service, _, _, _) = await BuildAsync();
        using var _h = h;

        var result = await service.ValidateListingRegionAsync(999999);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Validation, result.Error);
    }
}
