using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Listings.Search;
using Ovcuprim.Application.Stores;
using Ovcuprim.Domain.Enums;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ovcuprim.Api.IntegrationTests;

/// <summary>
/// The storefront surface end to end: who can see what, what a shared cache may keep, and what a
/// suspended store stops claiming.
/// </summary>
public class StoreEndpointsTests
{
    private const string SellerPhone = "+994501234567";
    private const string BuyerPhone = "+994551234567";
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

        var fresh = factory.CreateApiClient();
        await fresh.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(phone));
        var verify = await fresh.PostAsJsonAsync("/api/v1/auth/verify",
            new VerifyOtpRequest(phone, factory.Sms.LastCodeFor(phone), OtpPurpose.Login));

        var auth = (await verify.Content.ReadFromJsonAsync<AuthResponse>())!;
        fresh.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return fresh;
    }

    private static MultipartFormDataContent ImageUpload()
    {
        using var image = new Image<Rgba32>(640, 480);
        using var buffer = new MemoryStream();
        image.SaveAsJpeg(buffer);

        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(buffer.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "logo.jpg");

        return content;
    }

    private sealed record Storefront(Guid Id, string Slug, HttpClient Seller, HttpClient Admin);

    /// <summary>Applies as a seller and approves it, leaving a live storefront.</summary>
    private static async Task<Storefront> ActiveStoreAsync(ApiFactory factory)
    {
        var seller = await SignedInAsync(factory, SellerPhone);
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var applied = await seller.PostAsJsonAsync("/api/v1/me/store",
            new ApplyForStoreRequest("Ovçu Dünyası", "Ov və kamp avadanlıqları.", "Bakı ş.", "0501234567"));

        var store = (await applied.Content.ReadFromJsonAsync<StoreOwnerDto>())!;

        var approved = await admin.PostAsync($"/api/v1/admin/stores/{store.Id}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);

        return new Storefront(store.Id, store.Slug, seller, admin);
    }

    private static async Task<long> StoreListingAsync(ApiFactory factory, Storefront store)
    {
        var created = await store.Seller.PostAsJsonAsync("/api/v1/listings", new CreateListingRequest(
            CategorySlug, RegionSlug, "Mağaza çantası", "Mağazadan satılır.",
            120m, "Used", null, false, "0501234567", true, null, UseStore: true));

        var draft = (await created.Content.ReadFromJsonAsync<ListingDetailDto>())!;

        await store.Seller.PostAsync($"/api/v1/listings/{draft.Id}/media", ImageUpload());
        await store.Seller.PostAsJsonAsync($"/api/v1/listings/{draft.Id}/publish", new PublishListingRequest(false));
        await store.Admin.PostAsync($"/api/v1/admin/moderation/{draft.Id}/approve", null);

        return draft.ShortId;
    }

    // ---- lifecycle and visibility ----------------------------------------------------------

    [Fact]
    public async Task An_application_is_invisible_until_it_is_approved()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        var applied = await seller.PostAsJsonAsync("/api/v1/me/store",
            new ApplyForStoreRequest("Ovçu Dünyası", null, null, null));

        Assert.Equal(HttpStatusCode.Created, applied.StatusCode);

        var store = (await applied.Content.ReadFromJsonAsync<StoreOwnerDto>())!;
        var anonymous = factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/stores/{store.Slug}")).StatusCode);
        Assert.Empty((await anonymous.GetFromJsonAsync<PagedResult<StoreCardDto>>("/api/v1/stores"))!.Items);

        // The owner still sees their own pending application.
        Assert.Equal(HttpStatusCode.OK, (await seller.GetAsync("/api/v1/me/store")).StatusCode);
    }

    [Fact]
    public async Task An_approved_store_appears_publicly_and_in_the_directory()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);
        var anonymous = factory.CreateApiClient();

        var page = await anonymous.GetFromJsonAsync<StorePublicDto>($"/api/v1/stores/{store.Slug}");
        var directory = await anonymous.GetFromJsonAsync<PagedResult<StoreCardDto>>("/api/v1/stores");

        Assert.Equal("Ovçu Dünyası", page!.Name);
        Assert.Single(directory!.Items);
    }

    [Fact]
    public async Task Applying_needs_an_account_and_admin_routes_need_an_administrator()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);
        var anonymous = factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/v1/me/store", new ApplyForStoreRequest("X", null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/stores")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await store.Seller.GetAsync("/api/v1/admin/stores")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await store.Admin.GetAsync("/api/v1/admin/stores?status=Active")).StatusCode);
    }

    [Fact]
    public async Task A_rejected_application_disappears_and_the_seller_may_apply_again()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var applied = await seller.PostAsJsonAsync("/api/v1/me/store",
            new ApplyForStoreRequest("Ovçu Dünyası", null, null, null));
        var store = (await applied.Content.ReadFromJsonAsync<StoreOwnerDto>())!;

        var rejected = await admin.PostAsJsonAsync($"/api/v1/admin/stores/{store.Id}/reject",
            new RejectStoreRequest("Ad qaydalara uyğun deyil."));

        Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await seller.GetAsync("/api/v1/me/store")).StatusCode);

        var again = await seller.PostAsJsonAsync("/api/v1/me/store",
            new ApplyForStoreRequest("Düzəldilmiş Ad", null, null, null));

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Contains("store.rejected", await factory.GetAuditActionsAsync());
    }

    [Fact]
    public async Task Rejecting_without_a_reason_is_refused()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var applied = await seller.PostAsJsonAsync("/api/v1/me/store",
            new ApplyForStoreRequest("Ovçu Dünyası", null, null, null));
        var store = (await applied.Content.ReadFromJsonAsync<StoreOwnerDto>())!;

        var rejected = await admin.PostAsJsonAsync($"/api/v1/admin/stores/{store.Id}/reject",
            new RejectStoreRequest(""));

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    // ---- the suspended-store fallback --------------------------------------------------------

    [Fact]
    public async Task A_suspended_store_disappears_from_every_public_store_route()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);

        await store.Admin.PostAsJsonAsync($"/api/v1/admin/stores/{store.Id}/suspend",
            new SuspendStoreRequest("Yoxlama tələb olunur."));

        var anonymous = factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/stores/{store.Slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/stores/{store.Slug}/listings")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/stores/{store.Slug}/phone")).StatusCode);
        Assert.Empty((await anonymous.GetFromJsonAsync<PagedResult<StoreCardDto>>("/api/v1/stores"))!.Items);
    }

    [Fact]
    public async Task A_suspended_stores_listing_stays_live_but_stops_naming_the_store()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);
        var shortId = await StoreListingAsync(factory, store);

        var anonymous = factory.CreateApiClient();

        var before = await anonymous.GetFromJsonAsync<ListingPublicDto>($"/api/v1/listings/by-short-id/{shortId}");
        Assert.NotNull(before!.Store);

        await store.Admin.PostAsJsonAsync($"/api/v1/admin/stores/{store.Id}/suspend",
            new SuspendStoreRequest("Yoxlama tələb olunur."));

        var after = await anonymous.GetAsync($"/api/v1/listings/by-short-id/{shortId}");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);

        var page = (await after.Content.ReadFromJsonAsync<ListingPublicDto>())!;

        // The listing is still for sale; nothing claims the storefront is open.
        Assert.Null(page.Store);
        Assert.DoesNotContain(store.Slug, await after.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_suspended_stores_listing_is_still_in_the_catalogue()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);
        await StoreListingAsync(factory, store);

        await store.Admin.PostAsJsonAsync($"/api/v1/admin/stores/{store.Id}/suspend",
            new SuspendStoreRequest("Yoxlama."));

        var anonymous = factory.CreateApiClient();
        var page = await anonymous.GetFromJsonAsync<ListingSearchResultDto>("/api/v1/listings");

        Assert.Single(page!.Items);
    }

    [Fact]
    public async Task Reinstating_a_store_brings_the_storefront_back()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);

        await store.Admin.PostAsJsonAsync($"/api/v1/admin/stores/{store.Id}/suspend",
            new SuspendStoreRequest("Yoxlama."));
        await store.Admin.PostAsync($"/api/v1/admin/stores/{store.Id}/reinstate", null);

        var anonymous = factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/v1/stores/{store.Slug}")).StatusCode);
    }

    // ---- store listings ----------------------------------------------------------------------

    [Fact]
    public async Task A_store_listing_carries_the_store_block_and_the_store_seller_type()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);
        var shortId = await StoreListingAsync(factory, store);

        var anonymous = factory.CreateApiClient();
        var page = await anonymous.GetFromJsonAsync<ListingPublicDto>($"/api/v1/listings/by-short-id/{shortId}");

        Assert.Equal(store.Slug, page!.Store!.Slug);
        Assert.Equal(nameof(SellerType.Store), page.SellerType);
    }

    [Fact]
    public async Task The_seller_type_filter_finally_matches_something()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);
        await StoreListingAsync(factory, store);

        var anonymous = factory.CreateApiClient();

        var stores = await anonymous.GetFromJsonAsync<ListingSearchResultDto>("/api/v1/listings?sellerType=Store");
        var individuals = await anonymous.GetFromJsonAsync<ListingSearchResultDto>("/api/v1/listings?sellerType=Individual");

        Assert.Single(stores!.Items);
        Assert.Empty(individuals!.Items);
    }

    [Fact]
    public async Task The_storefront_grid_returns_only_that_stores_listings()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);
        await StoreListingAsync(factory, store);

        var anonymous = factory.CreateApiClient();
        var grid = await anonymous.GetFromJsonAsync<ListingSearchResultDto>($"/api/v1/stores/{store.Slug}/listings");

        Assert.Single(grid!.Items);
        Assert.Equal("Mağaza çantası", grid.Items[0].Title);
    }

    [Fact]
    public async Task A_client_cannot_claim_to_be_a_store()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        // No store at all, and the request asks to be filed under one.
        var payload = new Dictionary<string, object?>
        {
            ["categorySlug"] = CategorySlug,
            ["regionSlug"] = RegionSlug,
            ["title"] = "Saxta mağaza elanı",
            ["description"] = "Mağazasız satıcı.",
            ["price"] = 10m,
            ["condition"] = "Used",
            ["hasDelivery"] = false,
            ["contactPhone"] = "0501234567",
            ["showPhone"] = true,
            ["useStore"] = true,
            ["sellerType"] = "Store"
        };

        var response = await seller.PostAsJsonAsync("/api/v1/listings", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("useStore", out _));
    }

    // ---- caching -------------------------------------------------------------------------------

    [Fact]
    public async Task The_directory_is_shareable_but_the_store_page_is_not()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);
        var anonymous = factory.CreateApiClient();

        var directory = await anonymous.GetAsync("/api/v1/stores");
        var page = await anonymous.GetAsync($"/api/v1/stores/{store.Slug}");

        Assert.True(directory.Headers.CacheControl!.Public);
        Assert.Empty(directory.Headers.Vary);

        // The store page carries the caller's own follow state.
        Assert.True(page.Headers.CacheControl!.Private);
        Assert.False(page.Headers.CacheControl.Public);
        Assert.Contains("Authorization", page.Headers.Vary, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_storefront_grid_is_private_because_its_cards_carry_favourites()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);
        var anonymous = factory.CreateApiClient();

        var grid = await anonymous.GetAsync($"/api/v1/stores/{store.Slug}/listings");

        Assert.True(grid.Headers.CacheControl!.Private);
        Assert.Contains("Authorization", grid.Headers.Vary, StringComparer.OrdinalIgnoreCase);
    }

    // ---- privacy and following ------------------------------------------------------------------

    [Fact]
    public async Task The_public_store_never_exposes_the_owner()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);
        var anonymous = factory.CreateApiClient();

        var raw = await anonymous.GetStringAsync($"/api/v1/stores/{store.Slug}");

        Assert.DoesNotContain("ownerUserId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("994501234567", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Following_is_idempotent_and_scoped_to_the_follower()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);
        var buyer = await SignedInAsync(factory, BuyerPhone);

        Assert.Equal(HttpStatusCode.NoContent, (await buyer.PutAsync($"/api/v1/stores/{store.Slug}/follow", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await buyer.PutAsync($"/api/v1/stores/{store.Slug}/follow", null)).StatusCode);

        var theirs = await buyer.GetFromJsonAsync<StorePublicDto>($"/api/v1/stores/{store.Slug}");
        var owners = await store.Seller.GetFromJsonAsync<StorePublicDto>($"/api/v1/stores/{store.Slug}");

        Assert.True(theirs!.IsFollowing);
        Assert.False(owners!.IsFollowing);

        Assert.Single((await buyer.GetFromJsonAsync<PagedResult<StoreCardDto>>("/api/v1/me/followed-stores"))!.Items);

        Assert.Equal(HttpStatusCode.NoContent, (await buyer.DeleteAsync($"/api/v1/stores/{store.Slug}/follow")).StatusCode);
        Assert.Empty((await buyer.GetFromJsonAsync<PagedResult<StoreCardDto>>("/api/v1/me/followed-stores"))!.Items);
    }

    [Fact]
    public async Task Following_needs_an_account()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);
        var anonymous = factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsync($"/api/v1/stores/{store.Slug}/follow", null)).StatusCode);
    }

    [Fact]
    public async Task The_store_phone_is_revealed_only_on_request_and_is_throttled()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);
        var anonymous = factory.CreateApiClient();

        var page = await anonymous.GetFromJsonAsync<StorePublicDto>($"/api/v1/stores/{store.Slug}");
        Assert.DoesNotContain("1234567", page!.PhoneMasked!, StringComparison.Ordinal);

        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 25; i++)
        {
            statuses.Add((await anonymous.GetAsync($"/api/v1/stores/{store.Slug}/phone")).StatusCode);
        }

        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    // ---- owner management -------------------------------------------------------------------------

    [Fact]
    public async Task An_owner_can_edit_the_store_but_never_move_its_address()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);

        var updated = await store.Seller.PutAsJsonAsync("/api/v1/me/store",
            new UpdateStoreRequest("Tamamilə Yeni Ad", "Yeni təsvir.", "Gəncə ş.", "0501234567"));

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var mine = (await updated.Content.ReadFromJsonAsync<StoreOwnerDto>())!;

        Assert.Equal("Tamamilə Yeni Ad", mine.Name);
        Assert.Equal(store.Slug, mine.Slug);
    }

    [Fact]
    public async Task A_logo_upload_rejects_a_file_that_is_not_an_image()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);

        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent("not an image at all"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "logo.jpg");

        var response = await store.Seller.PostAsync("/api/v1/me/store/logo", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_logo_upload_is_stored_and_served_from_the_store_prefix()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);

        var response = await store.Seller.PostAsync("/api/v1/me/store/logo", ImageUpload());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var mine = (await response.Content.ReadFromJsonAsync<StoreOwnerDto>())!;

        Assert.NotNull(mine.LogoUrl);
        Assert.All(factory.Storage.Objects.Keys, key => Assert.StartsWith("stores/", key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Somebody_without_a_store_gets_a_miss_rather_than_a_forbidden()
    {
        using var factory = await SeededFactoryAsync();
        await ActiveStoreAsync(factory);

        var buyer = await SignedInAsync(factory, BuyerPhone);

        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync("/api/v1/me/store")).StatusCode);

        // A valid payload, so the answer is about ownership rather than about the request shape.
        Assert.Equal(HttpStatusCode.NotFound,
            (await buyer.PutAsJsonAsync("/api/v1/me/store",
                new UpdateStoreRequest("Başqasının Mağazası", null, null, null))).StatusCode);
    }

    // ---- storage reconciliation --------------------------------------------------------------

    [Fact]
    public async Task The_sweep_removes_a_stranded_store_image_but_never_a_live_one()
    {
        using var factory = await SeededFactoryAsync();
        var store = await ActiveStoreAsync(factory);

        await store.Seller.PostAsync("/api/v1/me/store/logo", ImageUpload());
        var live = factory.Storage.Objects.Keys.Single();

        // A file written by an upload whose row never committed.
        await factory.Storage.SaveAsync(
            new MemoryStream([1]), "stores/orphan/logo.webp", "image/webp");

        // Past the grace period, so an in-flight upload is not mistaken for a stranded one.
        factory.Clock.Advance(TimeSpan.FromDays(2));

        var maintenance = new ListingMaintenanceService(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<ILogger<ListingMaintenanceService>>());

        await maintenance.RunOnceAsync(CancellationToken.None);

        Assert.DoesNotContain("stores/orphan/logo.webp", factory.Storage.Objects.Keys);
        Assert.Contains(live, factory.Storage.Objects.Keys);
    }
}

