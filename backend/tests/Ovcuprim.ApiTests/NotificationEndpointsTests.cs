using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Notifications;
using Ovcuprim.Domain.Enums;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ovcuprim.Api.IntegrationTests;

/// <summary>
/// The in-app notification API through the real pipeline. <c>ListingEndpointsTests</c> and
/// <c>AdminEndpointsTests</c> cover the moderation decisions themselves; these are about what a
/// seller can read back afterwards.
/// </summary>
public class NotificationEndpointsTests
{
    private const string SellerPhone = "+994501234567";
    private const string OtherSellerPhone = "+994551234567";
    private const string ModeratorPhone = "+994701234567";
    private const string CategorySlug = "bel-cantasi";
    private const string RegionSlug = "baki";

    private static async Task<ApiFactory> SeededFactoryAsync()
    {
        var factory = new ApiFactory();
        await factory.SeedTaxonomyAsync();
        return factory;
    }

    /// <summary>
    /// A plain User keeps the registration session's own token — one OTP-request hit, not two. Only
    /// a role promotion pays for a second login round-trip, to pick up the new role claim. Several
    /// tests here sign in three accounts in one factory against the pinned 5-per-15-minute OTP
    /// limit, and every extra hit is a real one from a real client's perspective.
    /// </summary>
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

    /// <summary>Draft, image, publish — left pending for whichever test decides what happens next.</summary>
    private static async Task<Guid> PendingListingAsync(HttpClient seller)
    {
        var created = await seller.PostAsJsonAsync("/api/v1/listings", new CreateListingRequest(
            CategorySlug, RegionSlug, "Ov bel çantası 30L", "Az istifadə olunub.",
            150m, "Used", "Deuter", true, "0501234567", true, null));

        var draft = (await created.Content.ReadFromJsonAsync<ListingDetailDto>())!;

        await seller.PostAsync($"/api/v1/listings/{draft.Id}/media", JpegUpload());
        await seller.PostAsJsonAsync($"/api/v1/listings/{draft.Id}/publish", new PublishListingRequest(false));

        return draft.Id;
    }

    [Theory]
    [InlineData("GET", "/api/v1/me/notifications")]
    [InlineData("GET", "/api/v1/me/notifications/unread-count")]
    [InlineData("POST", "/api/v1/me/notifications/read-all")]
    public async Task Every_notification_route_requires_authentication(string method, string route)
    {
        using var factory = await SeededFactoryAsync();
        var anonymous = factory.CreateApiClient();

        var request = new HttpRequestMessage(new HttpMethod(method), route);
        var response = await anonymous.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_seller_is_notified_when_their_listing_is_approved()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var listingId = await PendingListingAsync(seller);
        await moderator.PostAsync($"/api/v1/admin/moderation/{listingId}/approve", null);

        var unread = await seller.GetFromJsonAsync<UnreadNotificationCountDto>("/api/v1/me/notifications/unread-count");
        Assert.Equal(1, unread!.Count);

        var mine = await seller.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/me/notifications");
        var notification = Assert.Single(mine!.Items);

        Assert.Equal("listing.approved", notification.Type);
        Assert.False(notification.IsRead);
    }

    [Fact]
    public async Task A_seller_is_notified_when_their_listing_is_rejected_with_the_reason()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var listingId = await PendingListingAsync(seller);
        await moderator.PostAsJsonAsync(
            $"/api/v1/admin/moderation/{listingId}/reject", new RejectListingRequest("Şəkillər kifayət deyil."));

        var mine = await seller.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/me/notifications");
        var notification = Assert.Single(mine!.Items);

        Assert.Equal("listing.rejected", notification.Type);
        Assert.Contains("Şəkillər kifayət deyil.", notification.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Marking_a_notification_read_updates_the_unread_count_and_is_idempotent()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var listingId = await PendingListingAsync(seller);
        await moderator.PostAsync($"/api/v1/admin/moderation/{listingId}/approve", null);

        var mine = await seller.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/me/notifications");
        var notificationId = mine!.Items[0].Id;

        var first = await seller.PostAsync($"/api/v1/me/notifications/{notificationId}/read", null);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await seller.PostAsync($"/api/v1/me/notifications/{notificationId}/read", null);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var unread = await seller.GetFromJsonAsync<UnreadNotificationCountDto>("/api/v1/me/notifications/unread-count");
        Assert.Equal(0, unread!.Count);
    }

    [Fact]
    public async Task A_seller_cannot_mark_another_sellers_notification_read()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var otherSeller = await SignedInAsync(factory, OtherSellerPhone);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var listingId = await PendingListingAsync(seller);
        await moderator.PostAsync($"/api/v1/admin/moderation/{listingId}/approve", null);

        var mine = await seller.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/me/notifications");
        var notificationId = mine!.Items[0].Id;

        var response = await otherSeller.PostAsync($"/api/v1/me/notifications/{notificationId}/read", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Read_all_clears_the_unread_count_for_every_notification()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var first = await PendingListingAsync(seller);
        var second = await PendingListingAsync(seller);
        await moderator.PostAsJsonAsync(
            $"/api/v1/admin/moderation/{first}/reject", new RejectListingRequest("Səbəb 1."));
        await moderator.PostAsJsonAsync(
            $"/api/v1/admin/moderation/{second}/reject", new RejectListingRequest("Səbəb 2."));

        var beforeUnread = await seller.GetFromJsonAsync<UnreadNotificationCountDto>("/api/v1/me/notifications/unread-count");
        Assert.Equal(2, beforeUnread!.Count);

        var readAll = await seller.PostAsync("/api/v1/me/notifications/read-all", null);
        Assert.Equal(HttpStatusCode.NoContent, readAll.StatusCode);

        var afterUnread = await seller.GetFromJsonAsync<UnreadNotificationCountDto>("/api/v1/me/notifications/unread-count");
        Assert.Equal(0, afterUnread!.Count);
    }
}
