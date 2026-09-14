using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Stores;
using Ovcuprim.Domain.Enums;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ovcuprim.Api.IntegrationTests;

/// <summary>
/// Endpoint-level regressions for the Phase 1–6 audit findings. Each test names the finding it
/// stands for, so a future change that reopens one fails with the reason attached.
/// </summary>
public class AuditRemediationPhase6Tests
{
    private const string SellerPhone = "+994501234567";
    private const string AdminPhone = "+994701234567";
    private const string CategorySlug = "bel-cantasi";
    private const string RegionSlug = "baki";

    private static async Task<ApiFactory> SeededFactoryAsync()
    {
        var factory = new ApiFactory();
        await factory.SeedTaxonomyAsync();
        return factory;
    }

    private static async Task<HttpClient> SignedInAsync(ApiFactory factory, string phone, UserRole role = UserRole.User)
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

    private static MultipartFormDataContent ImageUpload()
    {
        using var image = new Image<Rgba32>(320, 240);
        using var buffer = new MemoryStream();
        image.SaveAsJpeg(buffer);

        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(buffer.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");

        return content;
    }

    // ---- H-1: store writes answer with the field-error contract, never a database failure -------

    [Fact]
    public async Task H1_An_oversized_store_name_is_a_field_error_not_a_server_error()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        // 101 characters: one past the column width, which used to reach PostgreSQL.
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/store")
        {
            Content = JsonContent.Create(new ApplyForStoreRequest(new string('a', 101), null, null, null))
        };

        // Asked for explicitly, the way the auth suite does: the representation is negotiated, and
        // what matters is that the problem document is on offer.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));

        var response = await seller.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(problem.GetProperty("errors").TryGetProperty("name", out _));
    }

    [Theory]
    [InlineData("", null, null, null, "name")]
    [InlineData("X", null, null, null, "name")]
    [InlineData("Ovçu Dünyası", "over", null, null, "description")]
    [InlineData("Ovçu Dünyası", null, "over", null, "address")]
    [InlineData("Ovçu Dünyası", null, null, "12345", "phone")]
    public async Task H1_Every_store_field_is_bounded_before_it_reaches_the_database(
        string name, string? description, string? address, string? phone, string expectedField)
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        var response = await seller.PostAsJsonAsync("/api/v1/me/store", new ApplyForStoreRequest(
            name,
            description == "over" ? new string('a', 2001) : description,
            address == "over" ? new string('a', 201) : address,
            phone));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(problem.GetProperty("errors").TryGetProperty(expectedField, out _));
    }

    [Fact]
    public async Task H1_The_same_limits_apply_when_editing_an_existing_storefront()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        await seller.PostAsJsonAsync("/api/v1/me/store",
            new ApplyForStoreRequest("Ovçu Dünyası", null, null, null));

        var response = await seller.PutAsJsonAsync("/api/v1/me/store",
            new UpdateStoreRequest(new string('a', 101), null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task H1_A_name_of_exactly_the_column_width_is_still_accepted()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        var response = await seller.PostAsJsonAsync("/api/v1/me/store",
            new ApplyForStoreRequest(new string('a', 100), null, null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---- M-1: approve is for applications only ---------------------------------------------------

    [Fact]
    public async Task M1_Approve_will_not_reopen_a_suspended_storefront()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var applied = await seller.PostAsJsonAsync("/api/v1/me/store",
            new ApplyForStoreRequest("Ovçu Dünyası", null, null, null));
        var store = (await applied.Content.ReadFromJsonAsync<StoreOwnerDto>())!;

        await admin.PostAsync($"/api/v1/admin/stores/{store.Id}/approve", null);
        await admin.PostAsJsonAsync($"/api/v1/admin/stores/{store.Id}/suspend",
            new SuspendStoreRequest("Yoxlama tələb olunur."));

        var reopened = await admin.PostAsync($"/api/v1/admin/stores/{store.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, reopened.StatusCode);

        // Still hidden, and still reachable only through reinstate.
        var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/stores/{store.Slug}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/v1/admin/stores/{store.Id}/reinstate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/v1/stores/{store.Slug}")).StatusCode);
    }

    // ---- L-3: the queue refuses a status it does not recognise -----------------------------------

    [Fact]
    public async Task L3_An_unrecognised_queue_status_is_refused_rather_than_reinterpreted()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var bad = await admin.GetAsync("/api/v1/admin/stores?status=Rejected");
        var good = await admin.GetAsync("/api/v1/admin/stores?status=Suspended");

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
    }

    // ---- M-3: a listing sold through a storefront does not name the person behind it -------------

    private static async Task<long> StoreListingAsync(ApiFactory factory, HttpClient seller, HttpClient admin)
    {
        var created = await seller.PostAsJsonAsync("/api/v1/listings", new CreateListingRequest(
            CategorySlug, RegionSlug, "Mağaza çantası", "Mağazadan satılır.",
            120m, "Used", null, false, "0501234567", true, null, UseStore: true));

        var draft = (await created.Content.ReadFromJsonAsync<ListingDetailDto>())!;

        await seller.PostAsync($"/api/v1/listings/{draft.Id}/media", ImageUpload());
        await seller.PostAsJsonAsync($"/api/v1/listings/{draft.Id}/publish", new PublishListingRequest(false));
        await admin.PostAsync($"/api/v1/admin/moderation/{draft.Id}/approve", null);

        return draft.ShortId;
    }

    [Fact]
    public async Task M3_A_store_listing_does_not_disclose_the_owners_name()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var applied = await seller.PostAsJsonAsync("/api/v1/me/store",
            new ApplyForStoreRequest("Ovçu Dünyası", null, null, null));
        var store = (await applied.Content.ReadFromJsonAsync<StoreOwnerDto>())!;
        await admin.PostAsync($"/api/v1/admin/stores/{store.Id}/approve", null);

        var shortId = await StoreListingAsync(factory, seller, admin);

        var anonymous = factory.CreateApiClient();
        var response = await anonymous.GetAsync($"/api/v1/listings/by-short-id/{shortId}");
        var raw = await response.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<ListingPublicDto>(raw, JsonSerializerOptions.Web)!;

        // The shop is the public face; which person owns it is not on offer.
        Assert.NotNull(page.Store);
        Assert.Null(page.SellerName);
        Assert.DoesNotContain("Test İstifadəçi", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task M3_An_individual_listing_still_names_its_seller()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var created = await seller.PostAsJsonAsync("/api/v1/listings", new CreateListingRequest(
            CategorySlug, RegionSlug, "Şəxsi çanta", "Şəxsi satış.",
            80m, "Used", null, false, "0501234567", true, null));

        var draft = (await created.Content.ReadFromJsonAsync<ListingDetailDto>())!;

        await seller.PostAsync($"/api/v1/listings/{draft.Id}/media", ImageUpload());
        await seller.PostAsJsonAsync($"/api/v1/listings/{draft.Id}/publish", new PublishListingRequest(false));
        await admin.PostAsync($"/api/v1/admin/moderation/{draft.Id}/approve", null);

        var anonymous = factory.CreateApiClient();
        var page = await anonymous.GetFromJsonAsync<ListingPublicDto>(
            $"/api/v1/listings/by-short-id/{draft.ShortId}");

        Assert.Null(page!.Store);
        Assert.Equal("Test İstifadəçi", page.SellerName);
    }

    [Fact]
    public async Task M3_A_suspended_stores_listing_falls_back_to_the_seller_name()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var applied = await seller.PostAsJsonAsync("/api/v1/me/store",
            new ApplyForStoreRequest("Ovçu Dünyası", null, null, null));
        var store = (await applied.Content.ReadFromJsonAsync<StoreOwnerDto>())!;
        await admin.PostAsync($"/api/v1/admin/stores/{store.Id}/approve", null);

        var shortId = await StoreListingAsync(factory, seller, admin);

        await admin.PostAsJsonAsync($"/api/v1/admin/stores/{store.Id}/suspend",
            new SuspendStoreRequest("Yoxlama."));

        var anonymous = factory.CreateApiClient();
        var page = await anonymous.GetFromJsonAsync<ListingPublicDto>(
            $"/api/v1/listings/by-short-id/{shortId}");

        // D-5: the listing stays on sale, and with no store block the page needs a name to show.
        Assert.Null(page!.Store);
        Assert.Equal("Test İstifadəçi", page.SellerName);
    }

    // ---- M-4: a floor under the endpoints that declare no policy of their own --------------------

    [Theory]
    [InlineData("/api/v1/categories")]
    [InlineData("/api/v1/regions")]
    [InlineData("/api/v1/faq")]
    [InlineData("/api/v1/pages")]
    public async Task M4_Anonymous_taxonomy_reads_stay_public_and_cacheable(string route)
    {
        using var factory = await SeededFactoryAsync();
        var anonymous = factory.CreateApiClient();

        var response = await anonymous.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The floor must not have cost these their shared cacheability.
        Assert.True(response.Headers.CacheControl!.Public);
        Assert.Empty(response.Headers.Vary);
        Assert.NotNull(response.Headers.ETag);
    }

    [Fact]
    public async Task M4_An_unmetered_endpoint_is_no_longer_unbounded()
    {
        using var factory = await SeededFactoryAsync();
        var anonymous = factory.CreateApiClient();

        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 620; i++)
        {
            statuses.Add((await anonymous.GetAsync("/api/v1/categories")).StatusCode);
        }

        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task M4_The_floor_does_not_loosen_a_stricter_policy()
    {
        using var factory = await SeededFactoryAsync();
        var anonymous = factory.CreateApiClient();

        // otp-request allows 5 per 15 minutes. The floor is far looser, so it must not be what
        // decides here.
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 8; i++)
        {
            var response = await anonymous.PostAsJsonAsync("/api/v1/auth/register",
                new RegisterRequest($"+9945011122{i:D2}", "Test İstifadəçi"));

            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}

/// <summary>
/// M-5: which proxies may speak for a caller. Every rate limit partitions on the address this
/// produces, so the rules about what is trusted are worth pinning down directly.
/// </summary>
public class ForwardedHeadersSetupTests
{
    private static ForwardedHeadersSettings Settings(
        string[]? proxies = null, string[]? networks = null, int forwardLimit = 1) =>
        new()
        {
            KnownProxies = proxies ?? [],
            KnownNetworks = networks ?? [],
            ForwardLimit = forwardLimit
        };

    [Fact]
    public void Nothing_is_trusted_in_production_until_it_is_named()
    {
        var options = ForwardedHeadersSetup.BuildOptions(Settings(), isDevelopment: false);

        // The framework default trusts loopback; that default is cleared, so an unconfigured
        // production host ignores forwarded headers instead of believing them.
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public void Loopback_stays_trusted_in_development_so_local_runs_are_unchanged()
    {
        var options = ForwardedHeadersSetup.BuildOptions(Settings(), isDevelopment: true);

        Assert.Contains(System.Net.IPAddress.Loopback, options.KnownProxies);
    }

    [Fact]
    public void A_configured_proxy_replaces_the_development_default_rather_than_adding_to_it()
    {
        var options = ForwardedHeadersSetup.BuildOptions(
            Settings(proxies: ["10.0.0.4"]), isDevelopment: true);

        Assert.Equal(System.Net.IPAddress.Parse("10.0.0.4"), Assert.Single(options.KnownProxies));
    }

    [Fact]
    public void Networks_are_parsed_as_cidr()
    {
        var options = ForwardedHeadersSetup.BuildOptions(
            Settings(networks: ["10.0.0.0/16"]), isDevelopment: false);

        var network = Assert.Single(options.KnownIPNetworks);

        Assert.Equal(System.Net.IPAddress.Parse("10.0.0.0"), network.BaseAddress);
        Assert.Equal(16, network.PrefixLength);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("10.0.0.256")]
    public void A_malformed_proxy_address_fails_at_startup(string value)
    {
        // Loudly, rather than being dropped — a silently ignored entry is a limit sharing one bucket.
        Assert.Throws<InvalidOperationException>(
            () => ForwardedHeadersSetup.BuildOptions(Settings(proxies: [value]), isDevelopment: false));
    }

    [Theory]
    [InlineData("10.0.0.0")]
    [InlineData("10.0.0.0/mask")]
    [InlineData("10.0.0.0/33")]
    public void A_malformed_network_fails_at_startup(string value)
    {
        Assert.Throws<InvalidOperationException>(
            () => ForwardedHeadersSetup.BuildOptions(Settings(networks: [value]), isDevelopment: false));
    }

    [Fact]
    public void Only_the_address_and_scheme_headers_are_ever_honoured()
    {
        var options = ForwardedHeadersSetup.BuildOptions(Settings(), isDevelopment: false);

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.False(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
    }

    [Fact]
    public void The_forward_limit_comes_from_configuration()
    {
        // It has to match the number of proxies in front of the API, or a client-supplied prefix
        // of X-Forwarded-For could be read as the caller.
        Assert.Equal(1, ForwardedHeadersSetup.BuildOptions(Settings(), isDevelopment: false).ForwardLimit);
        Assert.Equal(2, ForwardedHeadersSetup.BuildOptions(Settings(forwardLimit: 2), isDevelopment: false).ForwardLimit);
    }
}
