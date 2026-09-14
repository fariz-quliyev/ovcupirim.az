using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Listings.Search;
using Ovcuprim.Domain.Enums;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ovcuprim.Api.IntegrationTests;

/// <summary>
/// The public discovery surface end to end: what an anonymous visitor can see, what needs a
/// signed-in account, and what needs a moderator.
/// </summary>
public class CatalogueEndpointsTests
{
    private const string SellerPhone = "+994501234567";
    private const string BuyerPhone = "+994551234567";
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
            // Registration already returns a token; a second OTP round would only spend limiter budget.
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

    private sealed record Live(Guid Id, long ShortId, HttpClient Seller, HttpClient Moderator);

    /// <summary>Draft, image, publish, approve — the only route onto the site.</summary>
    private static async Task<Live> PublishAsync(
        ApiFactory factory, string category = CategorySlug, bool ageConfirmed = false)
    {
        var seller = await SignedInAsync(factory, SellerPhone);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var created = await seller.PostAsJsonAsync("/api/v1/listings", new CreateListingRequest(
            category, RegionSlug, "Ov bel çantası 30L", "Az istifadə olunub.",
            150m, "Used", "Deuter", true, "0501234567", true, null));

        var draft = (await created.Content.ReadFromJsonAsync<ListingDetailDto>())!;

        await seller.PostAsync($"/api/v1/listings/{draft.Id}/media", JpegUpload());
        await seller.PostAsJsonAsync($"/api/v1/listings/{draft.Id}/publish", new PublishListingRequest(ageConfirmed));
        await moderator.PostAsync($"/api/v1/admin/moderation/{draft.Id}/approve", null);

        return new Live(draft.Id, draft.ShortId, seller, moderator);
    }

    [Fact]
    public async Task The_catalogue_is_open_to_anyone()
    {
        using var factory = await SeededFactoryAsync();
        await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();
        var response = await anonymous.GetAsync("/api/v1/listings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = (await response.Content.ReadFromJsonAsync<ListingSearchResultDto>())!;

        Assert.Single(page.Items);
        Assert.Equal(1, page.Total);
        Assert.True(page.TotalIsExact);
        Assert.Equal("newest", page.Sort);
    }

    [Fact]
    public async Task Only_live_listings_are_ever_returned()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();
        Assert.Single((await anonymous.GetFromJsonAsync<ListingSearchResultDto>("/api/v1/listings"))!.Items);

        // Retiring it takes it straight out of the catalogue.
        await live.Seller.PostAsync($"/api/v1/listings/{live.Id}/delete", null);

        Assert.Empty((await anonymous.GetFromJsonAsync<ListingSearchResultDto>("/api/v1/listings"))!.Items);
    }

    [Fact]
    public async Task A_card_never_carries_the_phone_number_or_the_description()
    {
        using var factory = await SeededFactoryAsync();
        await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();
        var raw = await anonymous.GetStringAsync("/api/v1/listings");

        Assert.DoesNotContain("994501234567", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("contactPhone", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Az istifadə olunub", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_category_filter_narrows_the_result()
    {
        using var factory = await SeededFactoryAsync();
        await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();

        var matching = await anonymous.GetFromJsonAsync<ListingSearchResultDto>(
            $"/api/v1/listings?category={CategorySlug}");
        var other = await anonymous.GetFromJsonAsync<ListingSearchResultDto>(
            "/api/v1/listings?category=gps");

        Assert.Single(matching!.Items);
        Assert.Empty(other!.Items);
    }

    [Fact]
    public async Task An_unknown_category_or_region_is_a_field_error()
    {
        using var factory = await SeededFactoryAsync();
        var anonymous = factory.CreateApiClient();

        var category = await anonymous.GetAsync("/api/v1/listings?category=yoxdur");
        var region = await anonymous.GetAsync("/api/v1/listings?region=yoxdur");

        Assert.Equal(HttpStatusCode.BadRequest, category.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, region.StatusCode);

        var problem = await region.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("region", out _));
    }

    [Fact]
    public async Task The_catalogue_answers_a_repeat_request_with_a_not_modified()
    {
        using var factory = await SeededFactoryAsync();
        await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();
        var first = await anonymous.GetAsync("/api/v1/listings");
        var etag = first.Headers.ETag!.ToString();

        anonymous.DefaultRequestHeaders.IfNoneMatch.ParseAdd(etag);
        var second = await anonymous.GetAsync("/api/v1/listings");

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Facets_are_public_and_count_both_dimensions()
    {
        using var factory = await SeededFactoryAsync();
        await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();
        var facets = await anonymous.GetFromJsonAsync<ListingFacetsDto>("/api/v1/listings/facets");

        Assert.Single(facets!.Categories);
        Assert.Single(facets.Regions);
        Assert.Equal(RegionSlug, facets.Regions[0].Slug);
    }

    [Fact]
    public async Task Similar_listings_never_include_the_listing_itself()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();
        var similar = await anonymous.GetFromJsonAsync<IReadOnlyList<ListingCardDto>>(
            $"/api/v1/listings/by-short-id/{live.ShortId}/similar");

        Assert.Empty(similar!);
    }

    [Fact]
    public async Task A_restricted_listing_is_listed_but_gated_when_opened()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory, category: "balta", ageConfirmed: true);

        var anonymous = factory.CreateApiClient();

        // Browsable, and the card says an acknowledgement will be needed.
        var page = await anonymous.GetFromJsonAsync<ListingSearchResultDto>("/api/v1/listings");
        Assert.True(Assert.Single(page!.Items).RequiresAgeConfirmation);

        var gated = await anonymous.GetAsync($"/api/v1/listings/by-short-id/{live.ShortId}");
        Assert.Equal(HttpStatusCode.BadRequest, gated.StatusCode);

        var problem = await gated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("ageConfirmation", out _));

        var opened = await anonymous.GetAsync($"/api/v1/listings/by-short-id/{live.ShortId}?ageConfirmed=true");
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task An_ordinary_listing_opens_without_a_gate()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();
        var opened = await anonymous.GetAsync($"/api/v1/listings/by-short-id/{live.ShortId}");

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task Favourites_need_an_account()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/v1/me/favorites")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsync($"/api/v1/me/favorites/{live.ShortId}", null)).StatusCode);
    }

    [Fact]
    public async Task Saving_and_unsaving_a_listing_is_idempotent()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);
        var buyer = await SignedInAsync(factory, BuyerPhone);

        Assert.Equal(HttpStatusCode.NoContent, (await buyer.PutAsync($"/api/v1/me/favorites/{live.ShortId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await buyer.PutAsync($"/api/v1/me/favorites/{live.ShortId}", null)).StatusCode);

        var saved = await buyer.GetFromJsonAsync<PagedResult<ListingCardDto>>("/api/v1/me/favorites");
        Assert.Single(saved!.Items);
        Assert.True(saved.Items[0].IsFavorited);

        Assert.Equal(HttpStatusCode.NoContent, (await buyer.DeleteAsync($"/api/v1/me/favorites/{live.ShortId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await buyer.DeleteAsync($"/api/v1/me/favorites/{live.ShortId}")).StatusCode);

        Assert.Empty((await buyer.GetFromJsonAsync<PagedResult<ListingCardDto>>("/api/v1/me/favorites"))!.Items);
    }

    [Fact]
    public async Task One_persons_saved_list_is_their_own()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);
        var buyer = await SignedInAsync(factory, BuyerPhone);

        await buyer.PutAsync($"/api/v1/me/favorites/{live.ShortId}", null);

        var sellersList = await live.Seller.GetFromJsonAsync<PagedResult<ListingCardDto>>("/api/v1/me/favorites");

        Assert.Empty(sellersList!.Items);
    }

    [Fact]
    public async Task Anyone_can_report_a_listing_but_only_a_moderator_sees_the_queue()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();

        var reported = await anonymous.PostAsJsonAsync(
            $"/api/v1/listings/by-short-id/{live.ShortId}/report",
            new SubmitReportRequest("Prohibited", "Qaydalara ziddir."));

        Assert.Equal(HttpStatusCode.NoContent, reported.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/reports")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await live.Seller.GetAsync("/api/v1/admin/reports")).StatusCode);

        var queue = await live.Moderator.GetFromJsonAsync<PagedResult<ReportDto>>("/api/v1/admin/reports");
        var item = Assert.Single(queue!.Items);

        Assert.True(item.IsAnonymous);
        Assert.Equal(live.ShortId, item.ListingShortId);
    }

    [Fact]
    public async Task Closing_a_report_records_who_did_it()
    {
        using var factory = await SeededFactoryAsync();
        var live = await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();
        await anonymous.PostAsJsonAsync(
            $"/api/v1/listings/by-short-id/{live.ShortId}/report", new SubmitReportRequest("Fraud", null));

        var queue = await live.Moderator.GetFromJsonAsync<PagedResult<ReportDto>>("/api/v1/admin/reports");
        var report = Assert.Single(queue!.Items);

        Assert.Equal(HttpStatusCode.NoContent,
            (await live.Moderator.PostAsync($"/api/v1/admin/reports/{report.Id}/resolve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await live.Moderator.PostAsync($"/api/v1/admin/reports/{report.Id}/dismiss", null)).StatusCode);

        Assert.Contains("report.resolved", await factory.GetAuditActionsAsync());
    }

    [Fact]
    public async Task Reporting_something_that_is_not_public_answers_like_a_miss()
    {
        using var factory = await SeededFactoryAsync();
        var anonymous = factory.CreateApiClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/v1/listings/by-short-id/987654/report", new SubmitReportRequest("Fraud", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_bad_sort_or_page_falls_back_instead_of_failing()
    {
        using var factory = await SeededFactoryAsync();
        await PublishAsync(factory);

        var anonymous = factory.CreateApiClient();
        var page = await anonymous.GetFromJsonAsync<ListingSearchResultDto>(
            "/api/v1/listings?sort=nonsense&page=-4&pageSize=9999");

        Assert.Equal("newest", page!.Sort);
        Assert.Equal(1, page.Page);
        Assert.Equal(PageRequest.MaxPageSize, page.PageSize);
    }
}
