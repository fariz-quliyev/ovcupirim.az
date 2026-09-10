using Ovcuprim.Application.Common;
using Ovcuprim.Application.Payments;

namespace Ovcuprim.Application.UnitTests.Payments;

public class PromotionPackageServiceTests
{
    [Fact]
    public async Task No_packages_are_seeded_the_catalog_starts_empty()
    {
        using var harness = new PromotionTestHarness();
        var active = await harness.Packages.GetActiveAsync();

        Assert.Empty(active);
    }

    [Fact]
    public async Task The_public_catalog_only_ever_shows_active_packages()
    {
        using var harness = new PromotionTestHarness();
        var active = await harness.ActivePackageAsync(code: "active-one");

        var inactiveResult = await harness.PackageAdmin.CreateAsync(
            new CreatePromotionPackageRequest("inactive-one", "Deaktiv paket", null, 7, 5m, 20));
        await harness.PackageAdmin.UpdateAsync(
            inactiveResult.Value!.Id, new UpdatePromotionPackageRequest("Deaktiv paket", null, 7, 5m, false, 20));

        var catalog = await harness.Packages.GetActiveAsync();

        Assert.Single(catalog);
        Assert.Equal(active.Code, catalog[0].Code);
    }

    [Fact]
    public async Task Admin_cannot_create_two_packages_with_the_same_code()
    {
        using var harness = new PromotionTestHarness();
        await harness.PackageAdmin.CreateAsync(new CreatePromotionPackageRequest("dup-code", "Paket A", null, 7, 5m, 10));

        var second = await harness.PackageAdmin.CreateAsync(
            new CreatePromotionPackageRequest("dup-code", "Paket B", null, 14, 8m, 20));

        Assert.False(second.Succeeded);
        Assert.Equal(ResultError.Conflict, second.Error);
    }

    [Fact]
    public async Task Updating_a_package_changes_its_price_and_duration()
    {
        using var harness = new PromotionTestHarness();
        var created = await harness.PackageAdmin.CreateAsync(
            new CreatePromotionPackageRequest("editable", "Paket", null, 7, 5m, 10));

        var updated = await harness.PackageAdmin.UpdateAsync(
            created.Value!.Id, new UpdatePromotionPackageRequest("Yenilənmiş paket", "Yeni təsvir", 14, 12.5m, true, 10));

        Assert.True(updated.Succeeded);
        Assert.Equal(14, updated.Value!.DurationDays);
        Assert.Equal(12.5m, updated.Value.PriceAzn);
        Assert.Equal("Yenilənmiş paket", updated.Value.NameAz);
    }
}
