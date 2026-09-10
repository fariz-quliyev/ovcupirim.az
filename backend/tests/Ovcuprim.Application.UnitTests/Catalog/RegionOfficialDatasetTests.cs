using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Regions;
using Ovcuprim.Domain.Enums;
using Ovcuprim.Infrastructure.Persistence;

namespace Ovcuprim.Application.UnitTests.Catalog;

/// <summary>
/// Integrity of <c>regions.official-2024.json</c> against what it claims to be: the State
/// Statistics Committee's "İnzibati ərazi bölgüsü təsnifatı, 2024", Level I only. See
/// docs/region-dataset.md for the full provenance and the source's own published totals these
/// counts are checked against.
/// </summary>
public class RegionOfficialDatasetTests
{
    private sealed record ImportPayload(List<RegionImportRow> Regions);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Reads the file exactly the way it ships — an embedded resource, the same as every other seed file.</summary>
    private static List<RegionImportRow> LoadOfficialDataset()
    {
        var assembly = Assembly.GetAssembly(typeof(AppDbContext))!;
        const string resource = "Ovcuprim.Infrastructure.Persistence.Seed.Data.regions.official-2024.json";

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource '{resource}' was not found.");

        var payload = JsonSerializer.Deserialize<ImportPayload>(stream, JsonOptions)
            ?? throw new InvalidOperationException("regions.official-2024.json deserialised to null.");

        return payload.Regions;
    }

    [Fact]
    public void The_file_has_exactly_the_officially_published_totals()
    {
        var rows = LoadOfficialDataset();

        Assert.Equal(75, rows.Count);

        // 10 mainland cities + Naxçıvan itself = the 11 the source publishes for this category;
        // 57 mainland rayons + 7 within Naxçıvan = the 64 the source publishes.
        Assert.Equal(11, rows.Count(r => r.Type is "City" or "AutonomousRepublic"));
        Assert.Equal(64, rows.Count(r => r.Type == "Rayon"));
    }

    [Fact]
    public void No_row_carries_a_coordinate()
    {
        // The 2024 classification is a code-and-name registry only. A coordinate appearing here
        // would mean one got invented somewhere between the source and this file.
        foreach (var row in LoadOfficialDataset())
        {
            Assert.False(row.Latitude.HasValue, $"{row.NameAz} unexpectedly carries a latitude.");
            Assert.False(row.Longitude.HasValue, $"{row.NameAz} unexpectedly carries a longitude.");
        }
    }

    [Fact]
    public void No_row_carries_a_Russian_name_or_a_parent()
    {
        // The source has no Russian column, and every imported row is deliberately top-level
        // (see docs/region-dataset.md's scope decision) — both would mean information not in the
        // source got added on the way in.
        foreach (var row in LoadOfficialDataset())
        {
            Assert.False(row.NameRu.HasValue, $"{row.NameAz} unexpectedly carries a Russian name.");
            Assert.Null(row.ParentSlug);
        }
    }

    [Fact]
    public void Every_row_states_isSelectable_and_none_are_silently_defaulted()
    {
        foreach (var row in LoadOfficialDataset())
        {
            Assert.True(row.IsSelectable.HasValue, $"{row.NameAz} does not state isSelectable.");
            Assert.True(row.IsSelectable!.Value);
        }
    }

    [Theory]
    [InlineData("Bakı", "City")]
    [InlineData("Naxçıvan", "AutonomousRepublic")]
    [InlineData("Şərur", "Rayon")]
    [InlineData("Xocalı", "Rayon")]
    [InlineData("Xocavənd", "Rayon")]
    [InlineData("Ağdərə", "Rayon")]
    public void Spot_checked_names_carry_the_expected_type(string nameAz, string type)
    {
        var row = LoadOfficialDataset().Single(r => r.NameAz == nameAz);
        Assert.Equal(type, row.Type);
    }

    [Fact]
    public void Xocali_and_Xocavend_are_two_distinct_rayons()
    {
        // Easy to conflate by eye; the source treats them as two different places (codes
        // 70100001 and 70300001) and this file must keep them apart.
        var rows = LoadOfficialDataset();

        Assert.Contains(rows, r => r.NameAz == "Xocalı" && r.Type == "Rayon");
        Assert.Contains(rows, r => r.NameAz == "Xocavənd" && r.Type == "Rayon");
    }

    [Fact]
    public void Every_name_produces_a_distinct_slug()
    {
        // ImportAsync keys existing rows by slug; two names folding to the same slug would make
        // the second silently overwrite the first instead of creating a separate region.
        var slugs = LoadOfficialDataset().Select(r => AzerbaijaniText.ToSlug(r.NameAz)).ToList();

        Assert.Equal(slugs.Count, slugs.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task The_dataset_imports_cleanly_into_a_fresh_database()
    {
        var harness = new TaxonomyTestHarness();
        using var _h = harness;
        // Production has no development region fixture (it is gated to Development only, the same
        // way the file under test here is gated to nothing but an explicit admin import) — seeding
        // without it is what makes "fresh database" actually mean fresh.
        await harness.Seeder.SeedAsync(includeDevelopmentRegions: false);

        var service = new RegionService(harness.Db, harness.Cache, harness.Clock);
        var rows = LoadOfficialDataset();

        var result = await service.ImportAsync(rows);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Value!.Errors);
        Assert.Equal(0, result.Value.Skipped);
        Assert.Equal(75, result.Value.Inserted);
        Assert.Equal(0, result.Value.Updated);

        var stored = await harness.Db.Regions.ToListAsync();

        // Every imported region landed exactly the way the scope decision says it should: flat,
        // selectable, uncoordinated.
        var imported = stored.Where(r => rows.Select(x => AzerbaijaniText.ToSlug(x.NameAz)).Contains(r.Slug)).ToList();
        Assert.Equal(75, imported.Count);
        Assert.All(imported, r => Assert.Equal(0, r.Depth));
        Assert.All(imported, r => Assert.Null(r.ParentId));
        Assert.All(imported, r => Assert.True(r.IsSelectable));
        Assert.All(imported, r => Assert.Null(r.Latitude));
        Assert.All(imported, r => Assert.Null(r.Longitude));
        Assert.All(imported, r => Assert.Null(r.NameRu));

        var baki = imported.Single(r => r.Slug == "baki");
        Assert.Equal(RegionType.City, baki.Type);

        var naxcivan = imported.Single(r => r.Slug == "naxcivan");
        Assert.Equal(RegionType.AutonomousRepublic, naxcivan.Type);

        Assert.Equal(64, imported.Count(r => r.Type == RegionType.Rayon));
    }

    [Fact]
    public async Task Re_importing_the_same_dataset_updates_rather_than_duplicates()
    {
        var harness = new TaxonomyTestHarness();
        using var _h = harness;
        await harness.Seeder.SeedAsync(includeDevelopmentRegions: false);

        var service = new RegionService(harness.Db, harness.Cache, harness.Clock);
        var rows = LoadOfficialDataset();

        var first = await service.ImportAsync(rows);
        var second = await service.ImportAsync(rows);

        Assert.Equal(75, first.Value!.Inserted);
        Assert.True(second.Succeeded);
        Assert.Equal(0, second.Value!.Inserted);
        Assert.Equal(75, second.Value.Updated);

        var total = await harness.Db.Regions.CountAsync(r => r.Depth == 0 && r.IsSelectable);
        Assert.Equal(75, total);
    }
}
