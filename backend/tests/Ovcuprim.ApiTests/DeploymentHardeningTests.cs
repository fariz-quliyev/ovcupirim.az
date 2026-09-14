using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Infrastructure;
using Ovcuprim.Application.Auth;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Api.IntegrationTests;

/// <summary>
/// G1: the settings that only matter in production, pinned so they cannot quietly regress.
/// </summary>
public class DeploymentHardeningTests
{
    // ---- AllowedHosts -------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";;")]
    public void An_unset_host_list_stops_a_production_host(string? configured)
    {
        // The framework treats this as "allow anything" and says nothing about it, which is the
        // worst shape for a setting whose only job is to be restrictive.
        var failure = Assert.Throws<InvalidOperationException>(
            () => HostFilteringSetup.Validate(configured, isDevelopment: false));

        Assert.Contains("AllowedHosts", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("ovcupirim.az;*")]
    public void A_wildcard_stops_a_production_host(string configured)
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => HostFilteringSetup.Validate(configured, isDevelopment: false));

        Assert.Contains("*", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Real_hostnames_are_accepted_and_reported()
    {
        var hosts = HostFilteringSetup.Validate("ovcupirim.az; www.ovcupirim.az ", isDevelopment: false);

        Assert.Equal(["ovcupirim.az", "www.ovcupirim.az"], hosts);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("")]
    [InlineData(null)]
    public void Development_is_left_alone(string? configured)
    {
        // Local work and this very test host reach Kestrel by whatever name they like.
        var hosts = HostFilteringSetup.Validate(configured, isDevelopment: true);

        Assert.NotNull(hosts);
    }

    // ---- Payments:FrontendBaseUrl --------------------------------------------------------------------

    /// <summary>
    /// Registers Infrastructure the way a production host does. Nothing is resolved, so no database
    /// or network is touched — this is about the registration-time guard alone.
    /// </summary>
    private static void AddProductionInfrastructure(string? frontendBaseUrl, bool gatewayKeys)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=unused;Database=unused;Username=unused;Password=unused",
            ["Payments:FrontendBaseUrl"] = frontendBaseUrl,
        };

        if (gatewayKeys)
        {
            settings["Payments:Epoint:PublicKey"] = "test-public-key";
            settings["Payments:Epoint:PrivateKey"] = "test-private-key";
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        new ServiceCollection().AddInfrastructure(configuration, isDevelopment: false);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/promotions")]
    [InlineData("ovcupirim.az")]
    [InlineData("ftp://ovcupirim.az")]
    public void A_configured_gateway_without_an_absolute_frontend_origin_stops_a_production_host(string? configured)
    {
        // Integration audit M-2: appsettings.json ships this blank on purpose, and blank would send the
        // gateway a relative browser-return URL. Real keys without a real origin must not start.
        var failure = Assert.Throws<InvalidOperationException>(
            () => AddProductionInfrastructure(configured, gatewayKeys: true));

        Assert.Contains("FrontendBaseUrl", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://ovcupirim.az")]
    [InlineData("http://staging.ovcupirim.az/")]
    public void A_real_frontend_origin_is_accepted_alongside_gateway_keys(string configured)
    {
        AddProductionInfrastructure(configured, gatewayKeys: true);
    }

    [Fact]
    public void Without_gateway_keys_the_placeholder_gateway_does_not_require_an_origin()
    {
        // A deployment that has not activated payments yet keeps the refusing placeholder and must
        // not be blocked on a value it does not use.
        AddProductionInfrastructure(null, gatewayKeys: false);
    }

    // ---- admin error responses are never cacheable ------------------------------------------------

    private static async Task<ApiFactory> SeededFactoryAsync()
    {
        var factory = new ApiFactory();
        await factory.SeedTaxonomyAsync();
        return factory;
    }

    private static async Task<HttpClient> SignedInAsync(ApiFactory factory, string phone, UserRole role)
    {
        var client = factory.CreateApiClient();

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(phone, "Test İstifadəçi"));
        var registration = await client.PostAsJsonAsync("/api/v1/auth/verify",
            new VerifyOtpRequest(phone, factory.Sms.LastCodeFor(phone), OtpPurpose.Registration));

        if (role == UserRole.User)
        {
            var registered = (await registration.Content.ReadFromJsonAsync<AuthResponse>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.AccessToken);

            return client;
        }

        await factory.SetRoleAsync(phone, role);

        // Sign in again so the promoted role is inside a freshly minted token — through whichever
        // door that role uses, since an Admin account is outside the SMS flow.
        return await factory.SignInAsync(phone, role);
    }

    [Fact]
    public async Task An_unauthenticated_admin_request_is_not_cacheable()
    {
        using var factory = await SeededFactoryAsync();
        var anonymous = factory.CreateApiClient();

        var response = await anonymous.GetAsync("/api/v1/admin/audit");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task A_forbidden_admin_request_is_not_cacheable()
    {
        using var factory = await SeededFactoryAsync();
        var moderator = await SignedInAsync(factory, "+994502222222", UserRole.Moderator);

        var response = await moderator.GetAsync("/api/v1/admin/audit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task A_throttled_response_is_not_cacheable()
    {
        using var factory = await SeededFactoryAsync();
        var anonymous = factory.CreateApiClient();

        HttpResponseMessage? limited = null;

        for (var i = 0; i < 10 && limited is null; i++)
        {
            var response = await anonymous.PostAsJsonAsync("/api/v1/auth/register",
                new RegisterRequest($"+9945011133{i:D2}", "Test İstifadəçi"));

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response;
            }
        }

        Assert.NotNull(limited);
        Assert.True(limited!.Headers.CacheControl!.NoStore);
    }

    // ---- the admin listing endpoint is metered like its siblings ------------------------------------

    [Fact]
    public async Task The_user_listing_is_metered_by_the_operator_policy()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, "+994703333333", UserRole.Admin);

        // admin-action allows 300/minute per operator; the global floor is looser, so exceeding
        // this proves the tighter policy is the one in force.
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 320; i++)
        {
            statuses.Add((await admin.GetAsync("/api/v1/users")).StatusCode);
        }

        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
