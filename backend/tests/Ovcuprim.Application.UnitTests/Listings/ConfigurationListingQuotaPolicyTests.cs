using Microsoft.Extensions.Configuration;
using Ovcuprim.Application.Listings;

namespace Ovcuprim.Application.UnitTests.Listings;

/// <summary>
/// The concrete policy production actually runs, as opposed to <see cref="StubQuotaPolicy"/> which
/// every other test in this project uses instead so the quota rules can be exercised without
/// configuration. This class is the one place B-1's "10 unless configured otherwise" is real.
/// </summary>
public class ConfigurationListingQuotaPolicyTests
{
    private static ConfigurationListingQuotaPolicy Build(Dictionary<string, string?> settings) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

    [Fact]
    public void An_unconfigured_host_gets_ten_not_unlimited()
    {
        var policy = Build([]);

        Assert.Equal(10, policy.LimitFor(categoryId: 1));
        Assert.Equal(ConfigurationListingQuotaPolicy.DefaultLimit, policy.LimitFor(categoryId: 1));
    }

    [Fact]
    public void A_configured_default_overrides_the_built_in_ten()
    {
        var policy = Build(new Dictionary<string, string?> { ["Listings:Quota:Default"] = "25" });

        Assert.Equal(25, policy.LimitFor(categoryId: 1));
    }

    [Fact]
    public void A_category_override_wins_over_the_default()
    {
        var policy = Build(new Dictionary<string, string?>
        {
            ["Listings:Quota:Default"] = "10",
            ["Listings:Quota:Categories:7"] = "3"
        });

        Assert.Equal(3, policy.LimitFor(categoryId: 7));
        Assert.Equal(10, policy.LimitFor(categoryId: 8));
    }

    [Fact]
    public void A_non_production_host_can_still_ask_for_effectively_unlimited()
    {
        // Not a sentinel — a plain large number, the same way Development loosens the OTP and
        // session rate limits. There is no magic "unlimited" value; a host that wants no practical
        // bound configures one large enough that it is never reached.
        var policy = Build(new Dictionary<string, string?> { ["Listings:Quota:Default"] = "1000000" });

        Assert.Equal(1_000_000, policy.LimitFor(categoryId: 1));
    }
}
