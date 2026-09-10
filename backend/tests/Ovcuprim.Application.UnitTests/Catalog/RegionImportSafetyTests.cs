using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Regions;

namespace Ovcuprim.Application.UnitTests.Catalog;

/// <summary>
/// M-1: a partial import must never erase what it does not mention.
/// </summary>
/// <remarks>
/// The authoritative dataset arrives in pieces — a coordinate correction here, a Russian name
/// there — and an update that assigned every optional field unconditionally silently cleared the
/// ones the incoming row happened to omit. Omitting a field now means "leave it"; sending an
/// explicit null means "clear it", and only that.
/// </remarks>
public class RegionImportSafetyTests
{
    private static async Task<(TaxonomyTestHarness Harness, IRegionService Service)> BuildAsync()
    {
        var harness = new TaxonomyTestHarness();
        await harness.SeedAsync();

        return (harness, new RegionService(harness.Db, harness.Cache, harness.Clock));
    }

    /// <summary>A fully specified row, as an authoritative first load would supply it.</summary>
    private static RegionImportRow Complete(string nameAz = "Masallı") =>
        new(nameAz, "Масаллы", null, "Rayon", null, IsSelectable: true, 39.0345, 48.6614, 10);

    /// <summary>The same place, mentioning only what is being corrected.</summary>
    private static RegionImportRow Partial(string nameAz = "Masallı") =>
        new(
            nameAz,
            Omittable<string>.Absent,
            null,
            "Rayon",
            null,
            IsSelectable: true,
            Omittable<double?>.Absent,
            Omittable<double?>.Absent,
            null);

    [Fact]
    public async Task A_first_load_stores_everything_it_was_given()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        await service.ImportAsync([Complete()]);

        var region = await h.Db.Regions.SingleAsync(r => r.Slug == "masalli");

        Assert.Equal("Масаллы", region.NameRu);
        Assert.Equal(39.0345, region.Latitude);
        Assert.Equal(48.6614, region.Longitude);
        Assert.Equal(10, region.SortOrder);
    }

    [Fact]
    public async Task A_partial_re_import_preserves_the_fields_it_omits()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        await service.ImportAsync([Complete()]);
        var second = await service.ImportAsync([Partial()]);

        var region = await h.Db.Regions.SingleAsync(r => r.Slug == "masalli");

        Assert.Equal(1, second.Value!.Updated);

        // This is the defect: before the fix, all three of these came back null.
        Assert.Equal("Масаллы", region.NameRu);
        Assert.Equal(39.0345, region.Latitude);
        Assert.Equal(48.6614, region.Longitude);
        Assert.Equal(10, region.SortOrder);
    }

    [Fact]
    public async Task An_explicit_null_is_the_only_thing_that_clears_a_field()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        await service.ImportAsync([Complete()]);

        // Stated, not omitted — the caller is asking for these to be emptied.
        await service.ImportAsync([new(
            "Masallı",
            new Omittable<string>(null),
            null,
            "Rayon",
            null,
            IsSelectable: true,
            new Omittable<double?>(null),
            new Omittable<double?>(null),
            null)]);

        var region = await h.Db.Regions.SingleAsync(r => r.Slug == "masalli");

        Assert.Null(region.NameRu);
        Assert.Null(region.Latitude);
        Assert.Null(region.Longitude);
    }

    [Fact]
    public async Task A_correction_changes_only_what_it_names()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        await service.ImportAsync([Complete()]);

        await service.ImportAsync([new(
            "Masallı",
            Omittable<string>.Absent,
            null,
            "Rayon",
            null,
            IsSelectable: true,
            new Omittable<double?>(39.1),
            Omittable<double?>.Absent,
            null)]);

        var region = await h.Db.Regions.SingleAsync(r => r.Slug == "masalli");

        Assert.Equal(39.1, region.Latitude);
        Assert.Equal(48.6614, region.Longitude);
        Assert.Equal("Масаллы", region.NameRu);
    }

    [Fact]
    public async Task The_required_decisions_still_have_to_be_stated()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        // isSelectable is a product decision, never inferred and never carried over.
        var result = await service.ImportAsync([new(
            "Masallı", Omittable<string>.Absent, null, "Rayon", null,
            IsSelectable: null, Omittable<double?>.Absent, Omittable<double?>.Absent, null)]);

        Assert.Equal(1, result.Value!.Skipped);
        Assert.Contains(result.Value.Errors, e => e.Contains("isSelectable", StringComparison.Ordinal));
        Assert.Empty(h.Db.Regions.Where(r => r.Slug == "masalli"));
    }

    [Fact]
    public async Task SortOrder_and_selectability_keep_the_protections_they_already_had()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        await service.ImportAsync([Complete()]);
        await service.ImportAsync([Partial()]);

        var kept = await h.Db.Regions.SingleAsync(r => r.Slug == "masalli");
        Assert.Equal(10, kept.SortOrder);

        // A stated value still wins, in both directions.
        await service.ImportAsync([Complete() with { SortOrder = 99, IsSelectable = false }]);

        var changed = await h.Db.Regions.SingleAsync(r => r.Slug == "masalli");

        Assert.Equal(99, changed.SortOrder);
        Assert.False(changed.IsSelectable);
    }

    [Fact]
    public async Task Nothing_in_the_import_can_delete_a_region()
    {
        var (h, service) = await BuildAsync();
        using var _h = h;

        await service.ImportAsync([Complete("Masallı"), Complete("Lerik")]);
        var before = await h.Db.Regions.CountAsync();

        // A later dataset that mentions only one of them leaves the other alone.
        await service.ImportAsync([Partial("Masallı")]);

        Assert.Equal(before, await h.Db.Regions.CountAsync());
        Assert.NotNull(await h.Db.Regions.SingleOrDefaultAsync(r => r.Slug == "lerik"));
    }
}

/// <summary>
/// The wire format behind M-1: JSON has to be able to say "I did not mention this field", and an
/// ordinary nullable cannot.
/// </summary>
public class OmittableJsonTests
{
    private sealed record Probe(string Name, Omittable<string> Note, Omittable<double?> Value);

    private static Probe Read(string json) =>
        JsonSerializer.Deserialize<Probe>(json, JsonSerializerOptions.Web)!;

    [Fact]
    public void An_absent_property_is_absent()
    {
        var probe = Read("""{"name":"x"}""");

        Assert.False(probe.Note.HasValue);
        Assert.False(probe.Value.HasValue);
    }

    [Fact]
    public void An_explicit_null_is_present_and_null()
    {
        var probe = Read("""{"name":"x","note":null,"value":null}""");

        Assert.True(probe.Note.HasValue);
        Assert.Null(probe.Note.Value);
        Assert.True(probe.Value.HasValue);
        Assert.Null(probe.Value.Value);
    }

    [Fact]
    public void A_supplied_value_arrives_intact()
    {
        var probe = Read("""{"name":"x","note":"qeyd","value":39.5}""");

        Assert.Equal("qeyd", probe.Note.Value);
        Assert.Equal(39.5, probe.Value.Value);
    }

    [Fact]
    public void Or_is_the_whole_decision_in_one_place()
    {
        Assert.Equal("mövcud", Omittable<string>.Absent.Or("mövcud"));
        Assert.Null(new Omittable<string>(null).Or("mövcud"));
        Assert.Equal("yeni", new Omittable<string>("yeni").Or("mövcud"));
    }
}
