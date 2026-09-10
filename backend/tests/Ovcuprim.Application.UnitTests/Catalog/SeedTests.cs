using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Catalog;

/// <summary>
/// Runs the real seed files, so the approved matrix totals are enforced by the build rather than
/// checked by hand.
/// </summary>
public class SeedTests
{
    [Fact]
    public async Task Seeds_the_approved_taxonomy_totals()
    {
        using var h = new TaxonomyTestHarness();

        var report = await h.SeedAsync();

        Assert.Equal(58, report.Categories);
        Assert.Equal(157, report.AttributeDefinitions);
        Assert.Equal(389, report.AttributeOptions);
    }

    [Fact]
    public async Task Seeds_eight_top_level_and_fifty_subcategories()
    {
        using var h = new TaxonomyTestHarness();
        await h.SeedAsync();

        var top = await h.Db.Categories.CountAsync(c => c.ParentId == null);
        var subs = await h.Db.Categories.CountAsync(c => c.ParentId != null);
        var deeper = await h.Db.Categories.CountAsync(c => c.Depth > 1);

        Assert.Equal(8, top);
        Assert.Equal(50, subs);
        Assert.Equal(0, deeper);
    }

    [Fact]
    public async Task Is_idempotent()
    {
        using var h = new TaxonomyTestHarness();

        await h.SeedAsync();
        var second = await h.SeedAsync();

        Assert.Equal(0, second.Total);
        Assert.Equal(58, await h.Db.Categories.CountAsync());
        Assert.Equal(157, await h.Db.AttributeDefinitions.CountAsync());
        Assert.Equal(389, await h.Db.AttributeOptions.CountAsync());
    }

    [Fact]
    public async Task Every_slug_is_url_safe_and_unique()
    {
        using var h = new TaxonomyTestHarness();
        await h.SeedAsync();

        var slugs = await h.Db.Categories.Select(c => c.Slug).ToListAsync();

        Assert.All(slugs, s => Assert.True(AzerbaijaniText.IsSlug(s), s));
        Assert.Equal(slugs.Count, slugs.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Every_option_value_is_slug_safe()
    {
        using var h = new TaxonomyTestHarness();
        await h.SeedAsync();

        var values = await h.Db.AttributeOptions.Select(o => o.Value).Distinct().ToListAsync();

        Assert.All(values, v => Assert.True(AzerbaijaniText.IsSlug(v), v));
    }

    [Fact]
    public async Task Produces_exactly_thirty_one_distinct_numeric_filterable_keys()
    {
        using var h = new TaxonomyTestHarness();
        await h.SeedAsync();

        var keys = await h.Db.AttributeDefinitions
            .Where(a => a.DataType == AttributeDataType.Number && a.IsFilterable)
            .Select(a => a.Key)
            .Distinct()
            .ToListAsync();

        // One expression index is created per key; the migration must stay in step with this.
        Assert.Equal(31, keys.Count);
    }

    [Fact]
    public async Task No_numeric_key_carries_two_different_units()
    {
        using var h = new TaxonomyTestHarness();
        await h.SeedAsync();

        var conflicts = await h.Db.AttributeDefinitions
            .Where(a => a.DataType == AttributeDataType.Number && a.IsFilterable)
            .GroupBy(a => a.Key)
            .Where(g => g.Select(a => a.Unit).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToListAsync();

        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task Spinning_is_merged_into_tilovlar_via_rod_type()
    {
        using var h = new TaxonomyTestHarness();
        await h.SeedAsync();

        Assert.False(await h.Db.Categories.AnyAsync(c => c.Slug == "spinning"));

        var rodType = await h.Db.AttributeDefinitions
            .Include(a => a.Options)
            .SingleAsync(a => a.Key == "rod_type");

        Assert.True(rodType.IsRequired);
        Assert.Contains(rodType.Options, o => o.Value == "spinning");
    }

    [Fact]
    public async Task Observation_scope_exists_and_no_riflescope_category_does()
    {
        using var h = new TaxonomyTestHarness();
        await h.SeedAsync();

        Assert.True(await h.Db.Categories.AnyAsync(c => c.Slug == "musahide-teleskopu"));
        Assert.False(await h.Db.Categories.AnyAsync(c => c.Slug == "teleskopik-optika"));
    }

    [Fact]
    public async Task No_firearm_option_exists_under_hunting_equipment()
    {
        using var h = new TaxonomyTestHarness();
        await h.SeedAsync();

        var equipment = await h.Db.AttributeDefinitions
            .Include(a => a.Options)
            .SingleAsync(a => a.Key == "equipment_type");

        var forbidden = new[] { "silah", "tufeng", "patron", "firearm", "ammo" };

        Assert.All(equipment.Options, option =>
            Assert.DoesNotContain(forbidden, f => option.Value.Contains(f, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Unclassified_categories_are_seeded_as_unclassified_not_guessed()
    {
        using var h = new TaxonomyTestHarness();
        await h.SeedAsync();

        var pending = await h.Db.Categories
            .Where(c => c.ParentId == null && c.RestrictionStatus == RestrictionStatus.Unclassified)
            .Select(c => c.Slug)
            .OrderBy(s => s)
            .ToListAsync();

        Assert.Equal(["bicaq-ve-alet", "optika-ve-durbin", "outdoor-neqliyyat"], pending);
    }

    [Fact]
    public async Task Legal_pages_are_seeded_unpublished()
    {
        using var h = new TaxonomyTestHarness();
        await h.SeedAsync();

        var legal = await h.Db.StaticPages
            .Where(p => p.Slug == "istifadeci-razilasmasi" || p.Slug == "mexfilik-siyaseti")
            .ToListAsync();

        Assert.Equal(2, legal.Count);
        Assert.All(legal, p => Assert.False(p.IsPublished));
    }

    [Fact]
    public async Task Development_regions_carry_no_invented_coordinates_or_counts()
    {
        using var h = new TaxonomyTestHarness();
        await h.SeedAsync();

        var regions = await h.Db.Regions.ToListAsync();

        Assert.NotEmpty(regions);
        Assert.All(regions, r =>
        {
            Assert.Null(r.Latitude);
            Assert.Null(r.Longitude);
            Assert.Equal(0, r.ListingCount);
        });
    }

    [Fact]
    public async Task Clothing_size_sets_are_identical_across_branches()
    {
        using var h = new TaxonomyTestHarness();
        await h.SeedAsync();

        var sets = await h.Db.AttributeDefinitions
            .Include(a => a.Options)
            .Where(a => a.Key == "size")
            .ToListAsync();

        var clothing = sets.Where(s => s.Options.Count == 7).ToList();

        // A4/A5/A6: shared option sets are referenced by name, so drift is impossible by construction.
        Assert.Equal(3, clothing.Count);
        var reference = clothing[0].Options.Select(o => o.Value).OrderBy(v => v).ToArray();
        Assert.All(clothing, s => Assert.Equal(reference, s.Options.Select(o => o.Value).OrderBy(v => v).ToArray()));
    }
}
