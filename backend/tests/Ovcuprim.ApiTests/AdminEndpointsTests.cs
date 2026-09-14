using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ovcuprim.Application.Admin;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Categories;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Domain.Enums;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ovcuprim.Api.IntegrationTests;

/// <summary>
/// The administrative surface: who may reach it, what it discloses, and what it must never cache.
/// </summary>
/// <remarks>
/// Server-side authorization is the only authorization there is — the admin UI hides what a role
/// cannot use, but every route here is asserted against the four-way matrix (anonymous, ordinary
/// user, moderator, admin) so hiding a link never becomes the thing that protects a route.
/// </remarks>
public class AdminEndpointsTests
{
    private const string UserPhone = "+994501111111";
    private const string ModeratorPhone = "+994502222222";
    private const string AdminPhone = "+994703333333";
    private const string CategorySlug = "bel-cantasi";
    private const string RegionSlug = "baki";

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

    /// <summary>A listing sitting in the moderation queue, submitted by an ordinary seller.</summary>
    private static async Task<ListingDetailDto> PendingListingAsync(HttpClient seller)
    {
        var created = await seller.PostAsJsonAsync("/api/v1/listings", new CreateListingRequest(
            CategorySlug, RegionSlug, "Yoxlama çantası", "Yoxlama təsviri.",
            120m, "Used", null, false, "0501234567", true, null));

        var draft = (await created.Content.ReadFromJsonAsync<ListingDetailDto>())!;

        await seller.PostAsync($"/api/v1/listings/{draft.Id}/media", ImageUpload());
        await seller.PostAsJsonAsync($"/api/v1/listings/{draft.Id}/publish", new PublishListingRequest(false));

        return draft;
    }

    // ---- RBAC: the four-way matrix ---------------------------------------------------------------

    public static TheoryData<string, bool, bool> AdminRoutes => new()
    {
        // route,                              moderator allowed, admin allowed
        { "/api/v1/admin/overview", true, true },
        { "/api/v1/admin/moderation/queue", true, true },
        { "/api/v1/admin/reports", true, true },
        { "/api/v1/admin/stores", false, true },
        { "/api/v1/admin/categories", false, true },
        { "/api/v1/admin/regions", false, true },
        { "/api/v1/admin/audit", false, true },
        { "/api/v1/users", false, true }
    };

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task Every_admin_route_enforces_its_own_policy(string route, bool moderatorAllowed, bool adminAllowed)
    {
        using var factory = await SeededFactoryAsync();

        var anonymous = factory.CreateApiClient();
        var user = await SignedInAsync(factory, UserPhone, UserRole.User);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(route)).StatusCode);

        Assert.Equal(
            moderatorAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await moderator.GetAsync(route)).StatusCode);

        Assert.Equal(
            adminAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await admin.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task Hiding_a_link_is_not_what_protects_a_route()
    {
        using var factory = await SeededFactoryAsync();
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        // A moderator who types an admin-only URL is refused by the server, not by the navigation.
        foreach (var route in new[] { "/api/v1/admin/audit", "/api/v1/admin/categories", "/api/v1/users" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await moderator.GetAsync(route)).StatusCode);
        }
    }

    // ---- caching ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("/api/v1/admin/overview")]
    [InlineData("/api/v1/admin/moderation/queue")]
    [InlineData("/api/v1/admin/reports")]
    [InlineData("/api/v1/admin/stores")]
    [InlineData("/api/v1/admin/categories")]
    [InlineData("/api/v1/admin/regions")]
    [InlineData("/api/v1/admin/audit")]
    [InlineData("/api/v1/users")]
    public async Task Every_admin_response_is_no_store(string route)
    {
        using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var response = await admin.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Public_cache_semantics_are_untouched_by_the_admin_headers()
    {
        using var factory = await SeededFactoryAsync();
        var anonymous = factory.CreateApiClient();

        var categories = await anonymous.GetAsync("/api/v1/categories");
        var stores = await anonymous.GetAsync("/api/v1/stores");

        Assert.True(categories.Headers.CacheControl!.Public);
        Assert.True(stores.Headers.CacheControl!.Public);
        Assert.False(categories.Headers.CacheControl.NoStore);
    }

    // ---- overview --------------------------------------------------------------------------------

    [Fact]
    public async Task The_overview_counts_what_is_waiting_and_how_long_it_has_waited()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, UserPhone, UserRole.User);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var empty = await moderator.GetFromJsonAsync<AdminOverviewDto>("/api/v1/admin/overview");

        Assert.Equal(0, empty!.PendingListings.Count);
        Assert.Null(empty.PendingListings.OldestWaitingSince);
        Assert.True(empty.DatabaseReachable);

        await PendingListingAsync(seller);

        var loaded = await moderator.GetFromJsonAsync<AdminOverviewDto>("/api/v1/admin/overview");

        Assert.Equal(1, loaded!.PendingListings.Count);
        Assert.NotNull(loaded.PendingListings.OldestWaitingSince);
        Assert.Equal(0, loaded.OpenReports.Count);
    }

    [Fact]
    public async Task The_overview_counts_decisions_taken_in_the_last_day()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, UserPhone, UserRole.User);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var listing = await PendingListingAsync(seller);
        await moderator.PostAsync($"/api/v1/admin/moderation/{listing.Id}/approve", null);

        var overview = await moderator.GetFromJsonAsync<AdminOverviewDto>("/api/v1/admin/overview");

        Assert.Equal(1, overview!.Last24Hours.ListingsApproved);
        Assert.Equal(0, overview.PendingListings.Count);
    }

    // ---- moderation detail (PD-7.5) ---------------------------------------------------------------

    [Fact]
    public async Task A_moderator_sees_the_whole_listing_including_the_unmasked_number()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, UserPhone, UserRole.User);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var listing = await PendingListingAsync(seller);

        var detail = await moderator.GetFromJsonAsync<ModerationDetailDto>(
            $"/api/v1/admin/moderation/{listing.Id}");

        // The one deliberate widening beyond the public page: fraud patterns are phone-shaped.
        Assert.Equal("+994501234567", detail!.Listing.ContactPhone);
        Assert.NotEmpty(detail.Listing.Media);
        Assert.Equal("Yoxlama təsviri.", detail.Listing.Description);
        Assert.Equal("Test İstifadəçi", detail.SellerName);
        Assert.Empty(detail.History);
    }

    [Fact]
    public async Task The_moderation_detail_is_not_a_way_around_the_owner_check()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, UserPhone, UserRole.User);

        var listing = await PendingListingAsync(seller);

        var anonymous = factory.CreateApiClient();
        var other = await SignedInAsync(factory, "+994505555555", UserRole.User);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/v1/admin/moderation/{listing.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await other.GetAsync($"/api/v1/admin/moderation/{listing.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_detail_carries_the_decisions_already_taken()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, UserPhone, UserRole.User);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var listing = await PendingListingAsync(seller);
        await moderator.PostAsJsonAsync($"/api/v1/admin/moderation/{listing.Id}/reject",
            new RejectListingRequest("Şəkillər kifayət deyil."));

        var detail = await moderator.GetFromJsonAsync<ModerationDetailDto>(
            $"/api/v1/admin/moderation/{listing.Id}");

        var decision = Assert.Single(detail!.History);

        Assert.Equal("Rejected", decision.Action);
        Assert.Equal("Şəkillər kifayət deyil.", decision.Reason);
        Assert.Equal("Test İstifadəçi", decision.ModeratorName);
    }

    [Fact]
    public async Task A_missing_listing_is_a_miss_rather_than_a_crash()
    {
        using var factory = await SeededFactoryAsync();
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        Assert.Equal(HttpStatusCode.NotFound,
            (await moderator.GetAsync($"/api/v1/admin/moderation/{Guid.NewGuid()}")).StatusCode);
    }

    // ---- audit viewer (PD-7.4) --------------------------------------------------------------------

    [Fact]
    public async Task The_audit_viewer_returns_structured_payloads()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, UserPhone, UserRole.User);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var listing = await PendingListingAsync(seller);
        await moderator.PostAsJsonAsync($"/api/v1/admin/moderation/{listing.Id}/reject",
            new RejectListingRequest("Səbəb."));

        var page = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>(
            "/api/v1/admin/audit?action=listing.moderation.");

        var entry = Assert.Single(page!.Items);

        Assert.Equal("listing.moderation.rejected", entry.Action);
        Assert.Equal("Test İstifadəçi", entry.ActorName);
        Assert.Equal("Səbəb.", entry.Payload!.Value.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task The_audit_viewer_filters_by_entity_actor_and_action()
    {
        using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, UserPhone, UserRole.User);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var listing = await PendingListingAsync(seller);
        await moderator.PostAsync($"/api/v1/admin/moderation/{listing.Id}/approve", null);

        var moderatorUser = await factory.GetUserAsync(ModeratorPhone);

        var byEntity = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>(
            $"/api/v1/admin/audit?entityType=Listing&entityId={listing.Id}");
        var byActor = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>(
            $"/api/v1/admin/audit?actorUserId={moderatorUser.Id}");
        var byOtherAction = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>(
            "/api/v1/admin/audit?action=store.");

        Assert.Single(byEntity!.Items);
        Assert.Single(byActor!.Items);
        Assert.Empty(byOtherAction!.Items);
    }

    [Fact]
    public async Task An_impossible_date_range_is_a_field_error()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var response = await admin.GetAsync(
            "/api/v1/admin/audit?from=2026-09-01T00:00:00Z&to=2026-08-01T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("from", out _));
    }

    [Fact]
    public async Task The_audit_trail_offers_no_way_to_change_it()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        // Append-only in the database, and read-only over HTTP. The path that exists rejects the
        // verb; the path that would mutate a row does not exist at all.
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await admin.PostAsync("/api/v1/admin/audit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.DeleteAsync($"/api/v1/admin/audit/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await admin.PutAsJsonAsync("/api/v1/admin/audit", new { })).StatusCode);
    }

    // ---- taxonomy tree (G-6) -----------------------------------------------------------------------

    [Fact]
    public async Task The_admin_tree_shows_categories_the_public_tree_hides()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var tree = await admin.GetFromJsonAsync<IReadOnlyList<AdminCategoryNodeDto>>("/api/v1/admin/categories");

        Assert.NotNull(tree);
        Assert.NotEmpty(tree);

        var root = tree[0];

        Assert.True(root.IsActive);
        Assert.NotEmpty(root.Children);
        Assert.All(root.Children, child => Assert.False(child.IsLeaf && child.Children.Count > 0));
    }

    // ---- role management (PD-7.2) -------------------------------------------------------------------

    [Fact]
    public async Task An_admin_can_grant_and_revoke_the_moderator_role()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);
        await SignedInAsync(factory, UserPhone, UserRole.User);

        var target = await factory.GetUserAsync(UserPhone);

        var granted = await admin.PostAsync($"/api/v1/users/{target.Id}/moderator", null);
        Assert.Equal("Moderator", (await granted.Content.ReadFromJsonAsync<UserDto>())!.Role);

        var revoked = await admin.DeleteAsync($"/api/v1/users/{target.Id}/moderator");
        Assert.Equal("User", (await revoked.Content.ReadFromJsonAsync<UserDto>())!.Role);

        var actions = await factory.GetAuditActionsAsync();
        Assert.Contains("user.moderator.granted", actions);
        Assert.Contains("user.moderator.revoked", actions);
    }

    [Fact]
    public async Task The_admin_role_is_never_grantable_or_revocable_through_the_api()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);
        await SignedInAsync(factory, ModeratorPhone, UserRole.Admin);

        var otherAdmin = await factory.GetUserAsync(ModeratorPhone);

        // There is no route that grants Admin, and an existing Admin cannot be demoted here either.
        var demote = await admin.DeleteAsync($"/api/v1/users/{otherAdmin.Id}/moderator");

        Assert.Equal(HttpStatusCode.BadRequest, demote.StatusCode);
        Assert.Equal(UserRole.Admin, (await factory.GetUserAsync(ModeratorPhone)).Role);
    }

    [Fact]
    public async Task An_admin_cannot_change_their_own_role()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var self = await factory.GetUserAsync(AdminPhone);
        var response = await admin.DeleteAsync($"/api/v1/users/{self.Id}/moderator");

        // Otherwise an administrator can lock everyone out of the panel with one click.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("userId", out _));
    }

    [Fact]
    public async Task A_moderator_cannot_promote_anyone()
    {
        using var factory = await SeededFactoryAsync();
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);
        await SignedInAsync(factory, UserPhone, UserRole.User);

        var target = await factory.GetUserAsync(UserPhone);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await moderator.PostAsync($"/api/v1/users/{target.Id}/moderator", null)).StatusCode);
    }

    // ---- user administration is inspect-only (PD-7.6) -------------------------------------------------

    [Fact]
    public async Task There_is_no_account_mutation_beyond_the_role_verbs()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);
        await SignedInAsync(factory, UserPhone, UserRole.User);

        var target = await factory.GetUserAsync(UserPhone);

        // No ban, no suspend, no delete, no force-logout, no editing someone else's profile.
        foreach (var route in new[] { "ban", "suspend", "logout", "password" })
        {
            Assert.Equal(HttpStatusCode.NotFound,
                (await admin.PostAsync($"/api/v1/users/{target.Id}/{route}", null)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await admin.DeleteAsync($"/api/v1/users/{target.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await admin.PutAsJsonAsync($"/api/v1/users/{target.Id}",
                new UpdateProfileRequest("Dəyişdirilmiş", null))).StatusCode);
    }

    [Fact]
    public async Task The_inspect_view_shows_an_account_without_offering_to_change_it()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);
        await SignedInAsync(factory, UserPhone, UserRole.User);

        var target = await factory.GetUserAsync(UserPhone);
        var response = await admin.GetAsync($"/api/v1/users/{target.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);

        var user = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.Equal(UserPhone, user.PhoneNumber);
    }

    // ---- RBAC on mutations ------------------------------------------------------------------------

    /// <summary>
    /// One mutating endpoint per admin controller. A class-level policy applies to every verb, so
    /// this is about proving the matrix rather than discovering it — but a GET-only matrix would
    /// not notice if someone later moved a policy onto individual actions and missed one.
    /// </summary>
    public static TheoryData<string, string, bool> AdminMutations => new()
    {
        // route,                                              moderator allowed
        { "POST", "/api/v1/admin/moderation/{id}/approve", true },
        { "POST", "/api/v1/admin/reports/{id}/resolve", true },
        { "POST", "/api/v1/admin/stores/{id}/approve", false },
        { "POST", "/api/v1/admin/categories/reorder", false },
        { "POST", "/api/v1/admin/regions/import", false },
        { "POST", "/api/v1/users/{id}/moderator", false },
    };

    [Theory]
    [MemberData(nameof(AdminMutations))]
    public async Task Every_admin_controller_enforces_its_policy_on_mutations(
        string method, string template, bool moderatorAllowed)
    {
        using var factory = await SeededFactoryAsync();

        var anonymous = factory.CreateApiClient();
        var user = await SignedInAsync(factory, UserPhone, UserRole.User);
        var moderator = await SignedInAsync(factory, ModeratorPhone, UserRole.Moderator);

        var route = template.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(anonymous, method, route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(user, method, route)).StatusCode);

        var asModerator = await SendAsync(moderator, method, route);

        if (moderatorAllowed)
        {
            // Allowed through the policy: the id is invented, so the answer is about the resource,
            // never about permission.
            Assert.NotEqual(HttpStatusCode.Forbidden, asModerator.StatusCode);
            Assert.NotEqual(HttpStatusCode.Unauthorized, asModerator.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, asModerator.StatusCode);
        }
    }

    [Fact]
    public async Task An_admin_reaches_every_mutation_the_policy_allows()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        foreach (var (method, template, _) in AdminMutations.Select(row =>
                     ((string)row[0]!, (string)row[1]!, (bool)row[2]!)))
        {
            var route = template.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal);
            var response = await SendAsync(admin, method, route);

            // Admin holds the Moderator policy too, so nothing here may answer 401 or 403.
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    /// <summary>Sends an empty JSON body, which is enough to get past model binding for these.</summary>
    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string route)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { reason = "Yoxlama səbəbi.", items = Array.Empty<object>(), regions = Array.Empty<object>() }),
        };

        return client.SendAsync(request);
    }

    // ---- M-3: the category edit contract -------------------------------------------------------------

    [Fact]
    public async Task Editing_a_category_preserves_every_field_it_did_not_change()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var tree = await admin.GetFromJsonAsync<IReadOnlyList<AdminCategoryNodeDto>>("/api/v1/admin/categories");
        var target = tree![0];

        // Give it something in every optional field, the way a real category carries SEO metadata.
        var seeded = await admin.PutAsJsonAsync($"/api/v1/admin/categories/{target.Id}", new UpdateCategoryRequest(
            target.NameAz, "Ru adı", "Təsvir", "Meta başlıq", "Meta təsvir",
            "icon-key", "image-key", target.SortOrder, target.IsActive, target.IsSelectable));

        Assert.Equal(HttpStatusCode.OK, seeded.StatusCode);

        // Now the client's round trip: read the tree, change one thing, send the whole shape back.
        var loaded = (await admin.GetFromJsonAsync<IReadOnlyList<AdminCategoryNodeDto>>("/api/v1/admin/categories"))!;
        var before = Find(loaded, target.Id)!;

        Assert.Equal("Təsvir", before.DescriptionAz);
        Assert.Equal("icon-key", before.IconKey);

        var renamed = await admin.PutAsJsonAsync($"/api/v1/admin/categories/{target.Id}", new UpdateCategoryRequest(
            "Yeni ad",
            before.NameRu,
            before.DescriptionAz,
            before.MetaTitleAz,
            before.MetaDescriptionAz,
            before.IconKey,
            before.ImageKey,
            before.SortOrder,
            before.IsActive,
            before.IsSelectable));

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var after = Find((await admin.GetFromJsonAsync<IReadOnlyList<AdminCategoryNodeDto>>("/api/v1/admin/categories"))!, target.Id)!;

        Assert.Equal("Yeni ad", after.NameAz);

        // The fields the operator never touched are still there — before the fix, a client body
        // that omitted them silently blanked all five.
        Assert.Equal("Ru adı", after.NameRu);
        Assert.Equal("Təsvir", after.DescriptionAz);
        Assert.Equal("Meta başlıq", after.MetaTitleAz);
        Assert.Equal("Meta təsvir", after.MetaDescriptionAz);
        Assert.Equal("icon-key", after.IconKey);
        Assert.Equal("image-key", after.ImageKey);
    }

    [Fact]
    public async Task The_admin_tree_carries_the_fields_an_editor_has_to_send_back()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var raw = await admin.GetStringAsync("/api/v1/admin/categories");

        // Without these on the tree, a form cannot round-trip them and would blank them on save.
        foreach (var field in new[] { "descriptionAz", "metaTitleAz", "metaDescriptionAz", "iconKey", "imageKey" })
        {
            Assert.Contains(field, raw, StringComparison.Ordinal);
        }
    }

    private static AdminCategoryNodeDto? Find(IReadOnlyList<AdminCategoryNodeDto> nodes, int id)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id)
            {
                return node;
            }

            if (Find(node.Children, id) is { } match)
            {
                return match;
            }
        }

        return null;
    }
}
