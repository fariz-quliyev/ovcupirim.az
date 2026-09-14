using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Listings;
using Ovcuprim.Domain.Enums;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ovcuprim.Api.IntegrationTests;

/// <summary>
/// Regressions for the Phase 1–5 handoff audit. Each test names the finding it locks down.
/// </summary>
public class AuditRemediationEndpointTests
{
    private const string SellerPhone = "+994501234567";
    private const string ModeratorPhone = "+994701234567";
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

    private static MultipartFormDataContent JpegUpload()
    {
        using var image = new Image<Rgba32>(640, 480);
        using var buffer = new MemoryStream();
        image.SaveAsJpeg(buffer);

        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(buffer.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");

        return content;
    }

    private sealed record Live(Guid Id, long ShortId, HttpClient Seller);

    private static async Task<Live> PublishAsync(ApiFactory factory)
    {
        var seller = await SignedInAsync(factory, SellerPhone);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var created = await seller.PostAsJsonAsync("/api/v1/listings", new CreateListingRequest(
            CategorySlug, RegionSlug, "Ov bel çantası 30L", "Az istifadə olunub.",
            150m, "Used", "Deuter", true, "0501234567", true, null));

        var draft = (await created.Content.ReadFromJsonAsync<ListingDetailDto>())!;

        await seller.PostAsync($"/api/v1/listings/{draft.Id}/media", JpegUpload());
        await seller.PostAsJsonAsync($"/api/v1/listings/{draft.Id}/publish", new PublishListingRequest(false));
        await moderator.PostAsync($"/api/v1/admin/moderation/{draft.Id}/approve", null);

        return new Live(draft.Id, draft.ShortId, seller);
    }

    // ---- H-2: the phone reveal must be metered -------------------------------------------------

    [Fact]
    public async Task H2_The_phone_reveal_is_throttled_long_before_a_scraper_finishes()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);
        var anonymous = factory.CreateApiClient();

        var statuses = new List<HttpStatusCode>();

        // The policy allows 20 an hour; a harvester walking ShortId would need thousands.
        for (var i = 0; i < 25; i++)
        {
            statuses.Add((await anonymous.GetAsync($"/api/v1/listings/by-short-id/{live.ShortId}/phone")).StatusCode);
        }

        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.Equal(20, statuses.Count(s => s == HttpStatusCode.OK));
    }

    [Fact]
    public async Task H2_The_phone_reveal_stays_anonymous()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);
        var anonymous = factory.CreateApiClient();

        var response = await anonymous.GetAsync($"/api/v1/listings/by-short-id/{live.ShortId}/phone");
        var revealed = (await response.Content.ReadFromJsonAsync<ListingPhoneDto>())!;

        // Metering it must not turn it into a sign-in wall.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("+994501234567", revealed.ContactPhone);
    }

    [Fact]
    public async Task H2_A_throttled_reveal_answers_with_the_standard_problem_document()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);
        var anonymous = factory.CreateApiClient();

        HttpResponseMessage? rejected = null;

        for (var i = 0; i < 25 && rejected is null; i++)
        {
            var response = await anonymous.GetAsync($"/api/v1/listings/by-short-id/{live.ShortId}/phone");
            rejected = response.StatusCode == HttpStatusCode.TooManyRequests ? response : null;
        }

        Assert.NotNull(rejected);
        Assert.Equal("application/problem+json", rejected!.Content.Headers.ContentType?.MediaType);
    }

    // ---- H-4: the public detail and its similar strip must be metered --------------------------

    [Fact]
    public async Task H4_Ordinary_browsing_of_a_listing_is_never_throttled()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);
        var anonymous = factory.CreateApiClient();

        // A person clicking through a few listings must not hit the limiter.
        for (var i = 0; i < 30; i++)
        {
            var response = await anonymous.GetAsync($"/api/v1/listings/by-short-id/{live.ShortId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task H4_Rapid_enumeration_of_the_detail_page_is_throttled()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);
        var anonymous = factory.CreateApiClient();

        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 130; i++)
        {
            statuses.Add((await anonymous.GetAsync($"/api/v1/listings/by-short-id/{live.ShortId}")).StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.Equal(120, statuses.Count(s => s == HttpStatusCode.OK));
    }

    [Fact]
    public async Task H4_View_counting_is_bounded_by_the_same_limiter()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);
        var anonymous = factory.CreateApiClient();

        for (var i = 0; i < 200; i++)
        {
            await anonymous.GetAsync($"/api/v1/listings/by-short-id/{live.ShortId}");
        }

        using var scope = factory.Services.CreateScope();
        var buffered = scope.ServiceProvider.GetRequiredService<IViewCountBuffer>().Drain();

        // Requests the limiter rejected never reach the service, so they never count as views.
        Assert.True(buffered[live.Id] <= 120, $"Expected at most 120 buffered views, saw {buffered[live.Id]}.");
    }

    [Fact]
    public async Task H4_The_similar_strip_shares_the_detail_limit()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);
        var anonymous = factory.CreateApiClient();

        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 130; i++)
        {
            statuses.Add((await anonymous.GetAsync($"/api/v1/listings/by-short-id/{live.ShortId}/similar")).StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    // ---- H-3: a per-visitor body must never be publicly cacheable ------------------------------

    [Fact]
    public async Task H3_The_catalogue_is_private_and_varies_by_authorization()
    {
        using var factory = await SeededFactoryAsync();
        await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();
        var response = await anonymous.GetAsync("/api/v1/listings");

        var cacheControl = response.Headers.CacheControl!;

        // isFavorited is the caller's own state, so a shared cache must not keep this.
        Assert.True(cacheControl.Private);
        Assert.False(cacheControl.Public);
        Assert.Contains("Authorization", response.Headers.Vary, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task H3_Facets_categories_and_regions_stay_shareable()
    {
        using var factory = await SeededFactoryAsync();
        await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();

        foreach (var path in new[] { "/api/v1/listings/facets", "/api/v1/categories", "/api/v1/regions" })
        {
            var response = await anonymous.GetAsync(path);
            var cacheControl = response.Headers.CacheControl!;

            Assert.True(cacheControl.Public, $"{path} should stay publicly cacheable.");
            Assert.Empty(response.Headers.Vary);
        }
    }

    [Fact]
    public async Task H3_The_catalogue_still_answers_a_repeat_request_with_a_not_modified()
    {
        using var factory = await SeededFactoryAsync();
        await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();
        var first = await anonymous.GetAsync("/api/v1/listings");

        anonymous.DefaultRequestHeaders.IfNoneMatch.ParseAdd(first.Headers.ETag!.ToString());
        var second = await anonymous.GetAsync("/api/v1/listings");

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    // ---- M-4: routes are advertised the way clients call them ----------------------------------

    [Fact]
    public async Task M4_Routes_are_advertised_in_lower_case()
    {
        using var factory = await SeededFactoryAsync();
        var anonymous = factory.CreateApiClient();

        var document = await anonymous.GetStringAsync("/openapi/v1.json");

        Assert.Contains("/api/v1/listings", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/categories", document, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/v1/Listings", document, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/v1/Categories", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task M4_Existing_lower_case_calls_keep_working()
    {
        using var factory = await SeededFactoryAsync();
        var anonymous = factory.CreateApiClient();

        foreach (var path in new[] { "/api/v1/categories", "/api/v1/regions", "/api/v1/listings", "/api/v1/health" })
        {
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(path)).StatusCode);
        }
    }

    // ---- M-2 / M-3: media cleanup --------------------------------------------------------------

    [Fact]
    public async Task M2_A_draft_the_seller_deleted_still_has_its_images_cleaned_up()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        var created = await seller.PostAsJsonAsync("/api/v1/listings", new CreateListingRequest(
            CategorySlug, RegionSlug, "Qaralama", "Silinəcək qaralama.",
            10m, "Used", null, false, "0501234567", true, null));

        var draft = (await created.Content.ReadFromJsonAsync<ListingDetailDto>())!;
        await seller.PostAsync($"/api/v1/listings/{draft.Id}/media", JpegUpload());

        // The seller removes it; the global query filter now hides it from ordinary queries.
        await seller.PostAsync($"/api/v1/listings/{draft.Id}/delete", null);

        await AgeEverythingAsync(factory, TimeSpan.FromDays(40));

        var storage = factory.Storage;
        var before = storage.Objects.Count;

        await RunMaintenanceAsync(factory);

        Assert.True(before > 0, "The upload should have written objects.");
        Assert.Empty(storage.Objects);
    }

    [Fact]
    public async Task M3_An_object_with_no_row_is_reconciled_away()
    {
        using var factory = await SeededFactoryAsync();
        var storage = factory.Storage;

        // Exactly what a failed delete leaves behind: files under the media prefix, no row.
        await storage.SaveAsync(new MemoryStream([1, 2, 3]), "listings/abandoned/cover.webp", "image/webp");
        factory.Clock.Advance(TimeSpan.FromDays(2));

        await RunMaintenanceAsync(factory);

        Assert.DoesNotContain("listings/abandoned/cover.webp", storage.Objects.Keys);
    }

    [Fact]
    public async Task M3_Live_media_is_never_reconciled_away()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);

        var storage = factory.Storage;

        // Age the live listing's objects past the grace period; they are still referenced.
        factory.Clock.Advance(TimeSpan.FromDays(2));

        var before = storage.Objects.Count;

        await RunMaintenanceAsync(factory);

        Assert.Equal(before, storage.Objects.Count);
        Assert.True(before > 0);
        _ = live;
    }

    [Fact]
    public async Task M3_A_fresh_upload_is_protected_by_the_grace_period()
    {
        using var factory = await SeededFactoryAsync();
        var storage = factory.Storage;

        // Written a moment ago with no row yet — exactly the state an in-flight upload is in.
        await storage.SaveAsync(new MemoryStream([1]), "listings/in-flight/cover.webp", "image/webp");
        // Written now, so the grace period still protects it.

        await RunMaintenanceAsync(factory);

        Assert.Contains("listings/in-flight/cover.webp", storage.Objects.Keys);
    }

    [Fact]
    public async Task M3_The_sweep_is_idempotent()
    {
        using var factory = await SeededFactoryAsync();
        var storage = factory.Storage;

        await storage.SaveAsync(new MemoryStream([1]), "listings/orphan/a.webp", "image/webp");
        factory.Clock.Advance(TimeSpan.FromDays(2));

        await RunMaintenanceAsync(factory);
        await RunMaintenanceAsync(factory);

        Assert.Empty(storage.Objects);
    }

    /// <summary>
    /// Moves the whole host forward rather than rewriting stored timestamps: SaveChanges stamps
    /// UpdatedAt on every modified row, so ageing a draft by editing it would undo itself.
    /// </summary>
    private static Task AgeEverythingAsync(ApiFactory factory, TimeSpan by)
    {
        factory.Clock.Advance(by);
        return Task.CompletedTask;
    }

    /// <summary>
    /// A fresh instance per pass. The service throttles its own sweeps by wall-clock interval, so
    /// reusing one would silently skip the second call and prove nothing about idempotency.
    /// </summary>
    private static async Task RunMaintenanceAsync(ApiFactory factory)
    {
        var maintenance = new ListingMaintenanceService(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<ILogger<ListingMaintenanceService>>());

        await maintenance.RunOnceAsync(CancellationToken.None);
    }
}
