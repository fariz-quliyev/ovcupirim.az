using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Domain.Enums;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ovcuprim.Api.IntegrationTests;

/// <summary>
/// End-to-end through the real pipeline: authentication, authorization, validation filter,
/// ProblemDetails and rate limiting all in place. The seeded taxonomy is the production one.
/// </summary>
public class ListingEndpointsTests
{
    private const string SellerPhone = "+994501234567";
    private const string OtherPhone = "+994551234567";
    private const string ModeratorPhone = "+994701234567";

    /// <summary>An unrestricted leaf from the real seed, with no required attributes.</summary>
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
        await client.PostAsJsonAsync("/api/v1/auth/verify",
            new VerifyOtpRequest(phone, factory.Sms.LastCodeFor(phone), OtpPurpose.Registration));

        if (role != UserRole.User)
        {
            await factory.SetRoleAsync(phone, role);
        }

        var fresh = factory.CreateApiClient();
        await fresh.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(phone));
        var verify = await fresh.PostAsJsonAsync("/api/v1/auth/verify",
            new VerifyOtpRequest(phone, factory.Sms.LastCodeFor(phone), OtpPurpose.Login));

        var auth = (await verify.Content.ReadFromJsonAsync<AuthResponse>())!;
        fresh.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return fresh;
    }

    private static CreateListingRequest NewListing(decimal? price = 150m) => new(
        CategorySlug,
        RegionSlug,
        "Ov bel çantası 30L",
        "Az istifadə olunub, su keçirməyən parça.",
        price,
        "Used",
        "Deuter",
        true,
        "0501234567",
        true,
        null);

    /// <summary>
    /// A real JPEG, because these tests run the production ImageSharp pipeline: the bytes are
    /// decoded, bounds-checked and re-encoded exactly as they would be in production.
    /// </summary>
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

    private static async Task<ListingDetailDto> CreateDraftAsync(HttpClient client, decimal? price = 150m)
    {
        var response = await client.PostAsJsonAsync("/api/v1/listings", NewListing(price));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ListingDetailDto>())!;
    }

    [Fact]
    public async Task Creating_a_listing_requires_authentication()
    {
        using var factory = await SeededFactoryAsync();
        var anonymous = factory.CreateApiClient();

        var response = await anonymous.PostAsJsonAsync("/api/v1/listings", NewListing());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_draft_is_created_and_is_not_publicly_visible()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        var draft = await CreateDraftAsync(seller);

        Assert.Equal(nameof(ListingStatus.Draft), draft.Status);

        var anonymous = factory.CreateApiClient();
        var publicView = await anonymous.GetAsync($"/api/v1/listings/by-short-id/{draft.ShortId}");

        Assert.Equal(HttpStatusCode.NotFound, publicView.StatusCode);
    }

    [Fact]
    public async Task The_full_flow_puts_a_listing_on_the_site()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var draft = await CreateDraftAsync(seller);

        var upload = await seller.PostAsync($"/api/v1/listings/{draft.Id}/media", JpegUpload());
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        var publish = await seller.PostAsJsonAsync(
            $"/api/v1/listings/{draft.Id}/publish", new PublishListingRequest(false));
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        var pending = (await publish.Content.ReadFromJsonAsync<ListingDetailDto>())!;
        Assert.Equal(nameof(ListingStatus.PendingModeration), pending.Status);

        var queue = await moderator.GetFromJsonAsync<PagedResult<ModerationQueueItemDto>>(
            "/api/v1/admin/moderation/queue");
        Assert.Single(queue!.Items);

        var approve = await moderator.PostAsync($"/api/v1/admin/moderation/{draft.Id}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);

        var anonymous = factory.CreateApiClient();
        var live = await anonymous.GetAsync($"/api/v1/listings/by-short-id/{draft.ShortId}");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        var page = (await live.Content.ReadFromJsonAsync<ListingPublicDto>())!;

        Assert.Equal("Ov bel çantası 30L", page.Title);
        Assert.Equal($"/elan/{page.Slug}-{page.ShortId}", page.CanonicalPath);
        Assert.Single(page.Media);
    }

    [Fact]
    public async Task The_public_page_masks_the_phone_and_reveals_it_only_on_request()
    {
        using var factory = await SeededFactoryAsync();
        var live = await LiveListingAsync(factory);

        var anonymous = factory.CreateApiClient();
        var page = await anonymous.GetFromJsonAsync<ListingPublicDto>($"/api/v1/listings/by-short-id/{live.ShortId}");

        Assert.DoesNotContain("1234567", page!.ContactPhoneMasked!, StringComparison.Ordinal);

        var revealed = await anonymous.GetFromJsonAsync<ListingPhoneDto>(
            $"/api/v1/listings/by-short-id/{live.ShortId}/phone");

        Assert.Equal("+994501234567", revealed!.ContactPhone);
    }

    [Fact]
    public async Task A_hidden_phone_is_not_revealed()
    {
        using var factory = await SeededFactoryAsync();
        var live = await LiveListingAsync(factory);

        var update = await live.Seller.PutAsJsonAsync($"/api/v1/listings/{live.Id}", UpdateFrom(showPhone: false));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var anonymous = factory.CreateApiClient();
        var revealed = await anonymous.GetAsync($"/api/v1/listings/by-short-id/{live.ShortId}/phone");

        Assert.Equal(HttpStatusCode.Forbidden, revealed.StatusCode);
    }

    [Fact]
    public async Task Another_user_gets_the_same_answer_as_a_miss()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var draft = await CreateDraftAsync(seller);

        var other = await SignedInAsync(factory, OtherPhone);

        var read = await other.GetAsync($"/api/v1/listings/{draft.Id}");
        var update = await other.PutAsJsonAsync($"/api/v1/listings/{draft.Id}", UpdateFrom());
        var delete = await other.PostAsync($"/api/v1/listings/{draft.Id}/delete", null);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task Overposted_server_owned_fields_are_ignored()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        // Everything the service owns, sent by a client that should not be trusted with it.
        var payload = new Dictionary<string, object?>
        {
            ["categorySlug"] = CategorySlug,
            ["regionSlug"] = RegionSlug,
            ["title"] = "Ov bel çantası 30L",
            ["description"] = "Az istifadə olunub.",
            ["price"] = 150m,
            ["condition"] = "Used",
            ["hasDelivery"] = true,
            ["contactPhone"] = "0501234567",
            ["showPhone"] = true,
            ["status"] = "Active",
            ["userId"] = Guid.NewGuid(),
            ["shortId"] = 999_999,
            ["viewCount"] = 5000,
            ["publishedAt"] = DateTimeOffset.UtcNow,
            ["sellerType"] = "Store"
        };

        var response = await seller.PostAsJsonAsync("/api/v1/listings", payload);
        var created = (await response.Content.ReadFromJsonAsync<ListingDetailDto>())!;

        Assert.Equal(nameof(ListingStatus.Draft), created.Status);
        Assert.Equal(0, created.ViewCount);
        Assert.Null(created.PublishedAt);
        Assert.NotEqual(999_999, created.ShortId);
        Assert.Equal(nameof(SellerType.Individual), created.SellerType);
    }

    [Fact]
    public async Task Field_errors_come_back_as_a_validation_problem()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        var response = await seller.PostAsJsonAsync("/api/v1/listings", NewListing() with { RegionSlug = "yoxdur" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(problem.GetProperty("errors").TryGetProperty("regionSlug", out _));
    }

    [Fact]
    public async Task An_oversized_title_is_rejected_by_the_validation_filter()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        var response = await seller.PostAsJsonAsync(
            "/api/v1/listings", NewListing() with { Title = new string('a', 71) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_null_price_round_trips_as_null()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        var draft = await CreateDraftAsync(seller, price: null);
        var free = await CreateDraftAsync(seller, price: 0m);

        Assert.Null(draft.Price);
        Assert.Equal(0m, free.Price);
    }

    [Fact]
    public async Task A_negative_price_is_rejected()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        var response = await seller.PostAsJsonAsync("/api/v1/listings", NewListing(price: -1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_non_image_upload_is_rejected()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var draft = await CreateDraftAsync(seller);

        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent("definitely not an image file at all"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "payload.jpg");

        var response = await seller.PostAsync($"/api/v1/listings/{draft.Id}/media", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Publishing_without_an_image_is_refused()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var draft = await CreateDraftAsync(seller);

        var response = await seller.PostAsJsonAsync(
            $"/api/v1/listings/{draft.Id}/publish", new PublishListingRequest(false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_seller_cannot_reach_the_moderation_queue()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        var response = await seller.GetAsync("/api/v1/admin/moderation/queue");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Rejecting_without_a_reason_is_refused()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var draft = await CreateDraftAsync(seller);
        await seller.PostAsync($"/api/v1/listings/{draft.Id}/media", JpegUpload());
        await seller.PostAsJsonAsync($"/api/v1/listings/{draft.Id}/publish", new PublishListingRequest(false));

        var response = await moderator.PostAsJsonAsync(
            $"/api/v1/admin/moderation/{draft.Id}/reject", new RejectListingRequest(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Moderation_decisions_are_audited()
    {
        using var factory = await SeededFactoryAsync();
        await LiveListingAsync(factory);

        var actions = await factory.GetAuditActionsAsync();

        Assert.Contains("listing.moderation.approved", actions);
    }

    [Fact]
    public async Task Deleting_a_live_listing_moves_it_to_the_expired_bucket()
    {
        using var factory = await SeededFactoryAsync();
        var live = await LiveListingAsync(factory);

        var deleted = await live.Seller.PostAsync($"/api/v1/listings/{live.Id}/delete", null);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var expired = await live.Seller.GetFromJsonAsync<PagedResult<ListingSummaryDto>>(
            "/api/v1/me/listings?status=expired");

        var card = Assert.Single(expired!.Items);
        Assert.NotNull(card.RestorableUntil);
        Assert.True(card.Can.Restore);

        var anonymous = factory.CreateApiClient();
        var gone = await anonymous.GetAsync($"/api/v1/listings/by-short-id/{live.ShortId}");

        // 404, not 410: the listing is restorable for 30 days, so it is not permanently gone.
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task There_is_no_seller_facing_hard_delete()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var draft = await CreateDraftAsync(seller);

        // Permanent removal is an administrative action; a seller only ever retires a listing.
        var hardDelete = await seller.DeleteAsync($"/api/v1/listings/{draft.Id}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, hardDelete.StatusCode);

        var retire = await seller.PostAsync($"/api/v1/listings/{draft.Id}/delete", null);

        Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
    }

    [Fact]
    public async Task Retiring_a_draft_removes_it_while_retiring_a_live_listing_keeps_it()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var draft = await CreateDraftAsync(seller);

        await seller.PostAsync($"/api/v1/listings/{draft.Id}/delete", null);

        var drafts = await seller.GetFromJsonAsync<PagedResult<ListingSummaryDto>>("/api/v1/me/listings?status=draft");
        var expired = await seller.GetFromJsonAsync<PagedResult<ListingSummaryDto>>("/api/v1/me/listings?status=expired");

        // A draft was never on the site, so it is gone rather than restorable.
        Assert.Empty(drafts!.Items);
        Assert.Empty(expired!.Items);
    }

    [Fact]
    public async Task My_listings_are_split_into_the_seller_buckets()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        await CreateDraftAsync(seller);

        var drafts = await seller.GetFromJsonAsync<PagedResult<ListingSummaryDto>>("/api/v1/me/listings?status=draft");
        var active = await seller.GetFromJsonAsync<PagedResult<ListingSummaryDto>>("/api/v1/me/listings?status=active");

        Assert.Single(drafts!.Items);
        Assert.Empty(active!.Items);
    }

    [Fact]
    public async Task Listing_limits_are_reported_for_the_signed_in_seller()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        var response = await seller.GetAsync("/api/v1/me/listing-limits");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Nothing published yet, so there is nothing to report — regardless of how generous the
        // configured limit is, GetUsageAsync only reports a category once it has actually been used.
        var limits = await response.Content.ReadFromJsonAsync<IReadOnlyList<ListingLimitDto>>();
        Assert.Empty(limits!);
    }

    // ---- B-1: the listing quota, end to end through the real pipeline --------------------------

    [Fact]
    public async Task A_seller_is_blocked_from_publishing_once_the_monthly_limit_is_reached()
    {
        using var factory = new ApiFactory(new Dictionary<string, string?>
        {
            // The one number the whole rest of this file relies on staying out of the way.
            ["Listings:Quota:Default"] = "1",
        });
        await factory.SeedTaxonomyAsync();

        var seller = await SignedInAsync(factory, SellerPhone);

        var first = await CreateDraftAsync(seller);
        await seller.PostAsync($"/api/v1/listings/{first.Id}/media", JpegUpload());
        var firstPublish = await seller.PostAsJsonAsync(
            $"/api/v1/listings/{first.Id}/publish", new PublishListingRequest(false));
        Assert.Equal(HttpStatusCode.OK, firstPublish.StatusCode);

        var second = await CreateDraftAsync(seller);
        await seller.PostAsync($"/api/v1/listings/{second.Id}/media", JpegUpload());
        var secondPublish = await seller.PostAsJsonAsync(
            $"/api/v1/listings/{second.Id}/publish", new PublishListingRequest(false));

        Assert.Equal(HttpStatusCode.Conflict, secondPublish.StatusCode);

        // The second draft was never accepted into moderation — the limit is on submission, not
        // on drafting.
        var pending = await seller.GetFromJsonAsync<PagedResult<ListingSummaryDto>>("/api/v1/me/listings?status=pending");
        Assert.Single(pending!.Items);

        var limits = await seller.GetFromJsonAsync<IReadOnlyList<ListingLimitDto>>("/api/v1/me/listing-limits");
        var limit = Assert.Single(limits!);
        Assert.Equal(1, limit.Used);
        Assert.Equal(1, limit.Limit);
        Assert.NotNull(limit.NextFreeSlotAt);
    }

    [Fact]
    public async Task Rejecting_a_listing_does_not_refund_its_quota_slot()
    {
        using var factory = new ApiFactory(new Dictionary<string, string?>
        {
            ["Listings:Quota:Default"] = "1",
        });
        await factory.SeedTaxonomyAsync();

        var seller = await SignedInAsync(factory, SellerPhone);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var draft = await CreateDraftAsync(seller);
        await seller.PostAsync($"/api/v1/listings/{draft.Id}/media", JpegUpload());
        await seller.PostAsJsonAsync($"/api/v1/listings/{draft.Id}/publish", new PublishListingRequest(false));

        await moderator.PostAsJsonAsync(
            $"/api/v1/admin/moderation/{draft.Id}/reject", new RejectListingRequest("Şəkillər kifayət deyil."));

        // Rejected, not deleted — the one free slot for this month was already spent submitting it,
        // and B-1 is explicit that a rejection does not give it back.
        var resubmit = await seller.PostAsJsonAsync(
            $"/api/v1/listings/{draft.Id}/publish", new PublishListingRequest(false));

        Assert.Equal(HttpStatusCode.Conflict, resubmit.StatusCode);

        var limits = await seller.GetFromJsonAsync<IReadOnlyList<ListingLimitDto>>("/api/v1/me/listing-limits");
        Assert.Equal(1, Assert.Single(limits!).Used);
    }

    // ---- Screening: duplicate detection, live and flag-only ------------------------------------

    [Fact]
    public async Task A_second_submission_with_the_same_title_is_flagged_for_the_moderator_not_blocked()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var first = await CreateDraftAsync(seller);
        await seller.PostAsync($"/api/v1/listings/{first.Id}/media", JpegUpload());
        await seller.PostAsJsonAsync($"/api/v1/listings/{first.Id}/publish", new PublishListingRequest(false));

        var second = await CreateDraftAsync(seller);
        await seller.PostAsync($"/api/v1/listings/{second.Id}/media", JpegUpload());
        var secondPublish = await seller.PostAsJsonAsync(
            $"/api/v1/listings/{second.Id}/publish", new PublishListingRequest(false));

        // Flagged, never blocked (B-2/B-3 planning, Rule 6): the second submission reaches the
        // queue exactly like the first.
        Assert.Equal(HttpStatusCode.OK, secondPublish.StatusCode);
        Assert.Equal(
            nameof(ListingStatus.PendingModeration),
            (await secondPublish.Content.ReadFromJsonAsync<ListingDetailDto>())!.Status);

        var queue = await moderator.GetFromJsonAsync<PagedResult<ModerationQueueItemDto>>(
            "/api/v1/admin/moderation/queue");

        var firstItem = queue!.Items.Single(i => i.Id == first.Id);
        var secondItem = queue.Items.Single(i => i.Id == second.Id);

        Assert.Empty(firstItem.ScreeningFlags);
        Assert.Contains(secondItem.ScreeningFlags, f => f.Contains("duplicate", StringComparison.Ordinal));

        var audit = await factory.GetAuditActionsAsync();
        Assert.Contains("listing.screening.flagged", audit);
    }

    [Fact]
    public async Task Media_can_be_reordered_and_the_cover_follows()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var draft = await CreateDraftAsync(seller);

        var first = (await (await seller.PostAsync($"/api/v1/listings/{draft.Id}/media", JpegUpload()))
            .Content.ReadFromJsonAsync<ListingMediaDto>())!;
        var second = (await (await seller.PostAsync($"/api/v1/listings/{draft.Id}/media", JpegUpload()))
            .Content.ReadFromJsonAsync<ListingMediaDto>())!;

        var response = await seller.PutAsJsonAsync(
            $"/api/v1/listings/{draft.Id}/media/order", new ReorderMediaRequest([second.Id, first.Id]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ordered = (await response.Content.ReadFromJsonAsync<IReadOnlyList<ListingMediaDto>>())!;

        Assert.Equal(second.Id, ordered[0].Id);
        Assert.True(ordered[0].IsPrimary);
        Assert.False(ordered[1].IsPrimary);
    }

    private static UpdateListingRequest UpdateFrom(bool showPhone = true) => new(
        RegionSlug,
        "Ov bel çantası 30L",
        "Az istifadə olunub, su keçirməyən parça.",
        150m,
        "Used",
        "Deuter",
        true,
        "0501234567",
        showPhone,
        null);

    /// <summary>
    /// Draft, image, publish, approve — the only path to a live listing. The signed-in clients come
    /// back so a caller never signs the same phone in twice (the second OTP would be a fresh one).
    /// </summary>
    private static async Task<LiveListing> LiveListingAsync(ApiFactory factory)
    {
        var seller = await SignedInAsync(factory, SellerPhone);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var draft = await CreateDraftAsync(seller);

        var upload = await seller.PostAsync($"/api/v1/listings/{draft.Id}/media", JpegUpload());
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        var publish = await seller.PostAsJsonAsync(
            $"/api/v1/listings/{draft.Id}/publish", new PublishListingRequest(false));
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        var approve = await moderator.PostAsync($"/api/v1/admin/moderation/{draft.Id}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);

        return new LiveListing(draft.ShortId, draft.Id, seller, moderator);
    }

    private sealed record LiveListing(long ShortId, Guid Id, HttpClient Seller, HttpClient Moderator);
}
