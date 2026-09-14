using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ovcuprim.Api.Controllers;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Categories;
using Ovcuprim.Application.Content;
using Ovcuprim.Application.Regions;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Api.IntegrationTests;

public class TaxonomyEndpointsTests
{
    private const string Phone = "+994501234567";

    private static async Task<ApiFactory> SeededFactoryAsync()
    {
        var factory = new ApiFactory();
        await factory.SeedTaxonomyAsync();
        return factory;
    }

    /// <summary>Registers, verifies and returns a client carrying a bearer token for the given role.</summary>
    private static async Task<HttpClient> AuthenticatedClientAsync(ApiFactory factory, UserRole role)
    {
        var client = factory.CreateApiClient();

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(Phone, "Test İstifadəçi"));
        await client.PostAsJsonAsync("/api/v1/auth/verify",
            new VerifyOtpRequest(Phone, factory.Sms.LastCodeFor(Phone), OtpPurpose.Registration));

        if (role != UserRole.User)
        {
            await factory.SetRoleAsync(Phone, role);
        }

        // Sign in again so the role is inside a freshly minted token — through whichever door that
        // role uses, since an Admin account is outside the SMS flow.
        return await factory.SignInAsync(Phone, role);
    }

    [Fact]
    public async Task Category_tree_is_public_and_matches_the_approved_shape()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/v1/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tree = (await response.Content.ReadFromJsonAsync<List<CategoryNodeDto>>())!;

        Assert.Equal(8, tree.Count);
        Assert.Equal(50, tree.Sum(c => c.Children.Count));
    }

    [Fact]
    public async Task Category_tree_answers_304_for_a_matching_etag()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateApiClient();

        var first = await client.GetAsync("/api/v1/categories");
        var etag = first.Headers.ETag!.ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/categories");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await client.SendAsync(request);

        Assert.NotNull(first.Headers.CacheControl);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task A_stale_etag_returns_the_full_body()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateApiClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/categories");
        request.Headers.TryAddWithoutValidation("If-None-Match", "\"stale\"");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Schema_endpoint_returns_inherited_and_own_attributes()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateApiClient();

        var schema = (await client.GetFromJsonAsync<CategorySchemaDto>("/api/v1/categories/tilovlar/schema"))!;
        var keys = schema.Attributes.Select(a => a.Key).ToList();

        Assert.Equal("tilovlar", schema.Category.Slug);
        Assert.Equal(["baliqciliq"], schema.Category.Path.Select(p => p.Slug).ToArray());
        Assert.True(schema.Category.IsLeaf);
        Assert.Contains("model", keys);      // inherited from the top-level category
        Assert.Contains("rod_type", keys);   // the spinning merge
        Assert.Contains("length", keys);
        Assert.True(schema.Attributes.Single(a => a.Key == "model").Inherited);
        Assert.False(schema.Attributes.Single(a => a.Key == "length").Inherited);
    }

    [Fact]
    public async Task Schema_exposes_a_multiselect_with_its_options()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateApiClient();

        var schema = (await client.GetFromJsonAsync<CategorySchemaDto>("/api/v1/categories/multitool/schema"))!;
        var features = schema.Attributes.Single(a => a.Key == "features");

        Assert.Equal("MultiSelect", features.DataType);
        Assert.Equal(7, features.Options!.Count);
    }

    [Fact]
    public async Task Schema_reflects_an_override_rather_than_the_inherited_option_set()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateApiClient();

        var boots = (await client.GetFromJsonAsync<CategorySchemaDto>("/api/v1/categories/ayaqqabi/schema"))!;
        var jacket = (await client.GetFromJsonAsync<CategorySchemaDto>("/api/v1/categories/godekce/schema"))!;

        Assert.Equal(14, boots.Attributes.Single(a => a.Key == "size").Options!.Count);
        Assert.False(boots.Attributes.Single(a => a.Key == "size").Inherited);
        Assert.Equal(7, jacket.Attributes.Single(a => a.Key == "size").Options!.Count);
        Assert.True(jacket.Attributes.Single(a => a.Key == "size").Inherited);
    }

    [Fact]
    public async Task An_unclassified_category_requires_age_confirmation()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateApiClient();

        var knife = (await client.GetFromJsonAsync<CategorySchemaDto>("/api/v1/categories/ov-bicagi/schema"))!;
        var rod = (await client.GetFromJsonAsync<CategorySchemaDto>("/api/v1/categories/tilovlar/schema"))!;

        Assert.Equal("Unclassified", knife.Category.RestrictionStatus);
        Assert.True(knife.Category.RequiresAgeConfirmation);
        Assert.Equal("Unrestricted", rod.Category.RestrictionStatus);
        Assert.False(rod.Category.RequiresAgeConfirmation);
    }

    [Fact]
    public async Task Unknown_slugs_return_404()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/categories/yoxdur")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/categories/yoxdur/schema")).StatusCode);
    }

    [Fact]
    public async Task Regions_and_faq_are_public()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateApiClient();

        var regions = (await client.GetFromJsonAsync<List<RegionDto>>("/api/v1/regions"))!;
        var faq = (await client.GetFromJsonAsync<List<FaqCategoryDto>>("/api/v1/faq"))!;

        Assert.NotEmpty(regions);
        Assert.NotEmpty(faq);
    }

    [Fact]
    public async Task An_unpublished_page_is_indistinguishable_from_a_missing_one()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateApiClient();

        var published = await client.GetAsync("/api/v1/pages/layihe-haqqinda");
        var draft = await client.GetAsync("/api/v1/pages/mexfilik-siyaseti");
        var missing = await client.GetAsync("/api/v1/pages/yoxdur");

        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, draft.StatusCode);
        Assert.Equal(missing.StatusCode, draft.StatusCode);
    }

    [Fact]
    public async Task Admin_mutations_reject_anonymous_and_ordinary_users()
    {
        using var factory = await SeededFactoryAsync();

        var anonymous = factory.CreateApiClient();
        var anonymousResponse = await anonymous.PostAsJsonAsync("/api/v1/admin/categories",
            new CreateCategoryRequest("Yeni", null, null, null, null, null, 900));

        var user = await AuthenticatedClientAsync(factory, UserRole.User);
        var userResponse = await user.PostAsJsonAsync("/api/v1/admin/categories",
            new CreateCategoryRequest("Yeni", null, null, null, null, null, 900));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, userResponse.StatusCode);
    }

    [Fact]
    public async Task An_admin_can_create_a_category_and_it_appears_in_the_tree()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AuthenticatedClientAsync(factory, UserRole.Admin);

        var response = await admin.PostAsJsonAsync("/api/v1/admin/categories",
            new CreateCategoryRequest("Test kateqoriya", null, null, null, null, null, 900));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = (await response.Content.ReadFromJsonAsync<CategoryDetailDto>())!;
        Assert.Equal("test-kateqoriya", created.Slug);
        // A new top-level category starts unclassified rather than silently cleared.
        Assert.Equal("Unclassified", created.RestrictionStatus);

        var tree = (await admin.GetFromJsonAsync<List<CategoryNodeDto>>("/api/v1/categories"))!;
        Assert.Contains(tree, c => c.Slug == "test-kateqoriya");
    }

    [Fact]
    public async Task A_duplicate_slug_is_rejected_with_409()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AuthenticatedClientAsync(factory, UserRole.Admin);

        var response = await admin.PostAsJsonAsync("/api/v1/admin/categories",
            new CreateCategoryRequest("Ovçuluq", null, null, null, null, null, 900));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_third_level_category_is_rejected()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AuthenticatedClientAsync(factory, UserRole.Admin);

        var tilovlar = (await admin.GetFromJsonAsync<CategoryDetailDto>("/api/v1/categories/tilovlar"))!;

        var response = await admin.PostAsJsonAsync("/api/v1/admin/categories",
            new CreateCategoryRequest("Üçüncü səviyyə", null, tilovlar.Id, null, null, null, 10));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Changing_a_restriction_status_is_reflected_in_the_children()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AuthenticatedClientAsync(factory, UserRole.Admin);

        var fishing = (await admin.GetFromJsonAsync<CategoryDetailDto>("/api/v1/categories/baliqciliq"))!;

        var response = await admin.PutAsJsonAsync($"/api/v1/admin/categories/{fishing.Id}/restriction",
            new SetRestrictionRequest("Restricted"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rod = (await admin.GetFromJsonAsync<CategorySchemaDto>("/api/v1/categories/tilovlar/schema"))!;
        Assert.Equal("Restricted", rod.Category.RestrictionStatus);
        Assert.True(rod.Category.RequiresAgeConfirmation);
    }

    /// <summary>Named arguments: isSelectable must be stated on every authoritative row.</summary>
    private static RegionImportRow Row(string nameAz, bool? isSelectable, string? parentSlug = null, int sortOrder = 500) =>
        new(NameAz: nameAz, NameRu: null, Slug: null, Type: "Rayon", ParentSlug: parentSlug,
            IsSelectable: isSelectable, Latitude: null, Longitude: null, SortOrder: sortOrder);

    [Fact]
    public async Task Region_import_rejects_a_row_without_an_explicit_isSelectable()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AuthenticatedClientAsync(factory, UserRole.Admin);

        var rows = new List<RegionImportRow> { Row("Şirvan", true, sortOrder: 520), Row("Naməlum", null, sortOrder: 530) };

        var response = await admin.PostAsJsonAsync("/api/v1/admin/regions/import", new RegionImportRequest(rows));
        var result = (await response.Content.ReadFromJsonAsync<RegionImportResult>())!;

        // Omitting the flag must never quietly publish an internal administrative record.
        Assert.Equal(1, result.Inserted);
        Assert.Equal(1, result.Skipped);
        Assert.Contains(result.Errors, e => e.Contains("isSelectable", StringComparison.Ordinal));

        var regions = (await admin.GetFromJsonAsync<List<RegionDto>>("/api/v1/regions"))!;
        Assert.DoesNotContain(regions, r => r.Slug == "namelum");
    }

    [Fact]
    public async Task An_imported_internal_record_is_stored_but_never_offered_to_users()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AuthenticatedClientAsync(factory, UserRole.Admin);

        var rows = new List<RegionImportRow>
        {
            Row("Abşeron", true, sortOrder: 540),
            Row("Nəsimi", false, parentSlug: "abseron", sortOrder: 550)
        };

        await admin.PostAsJsonAsync("/api/v1/admin/regions/import", new RegionImportRequest(rows));

        var publicRegions = (await admin.GetFromJsonAsync<List<RegionDto>>("/api/v1/regions"))!;
        var adminRegions = (await admin.GetFromJsonAsync<List<AdminRegionDto>>("/api/v1/admin/regions"))!;

        Assert.Contains(publicRegions, r => r.Slug == "abseron");
        Assert.DoesNotContain(publicRegions, r => r.Slug == "nesimi");

        var internalRecord = adminRegions.Single(r => r.Slug == "nesimi");
        Assert.False(internalRecord.IsSelectable);
        Assert.Equal(1, internalRecord.Depth);
        Assert.NotNull(internalRecord.ParentId);
    }

    [Fact]
    public async Task The_public_region_list_is_flat_and_free_of_administrative_detail()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateApiClient();

        var raw = await client.GetStringAsync("/api/v1/regions");
        var first = JsonDocument.Parse(raw).RootElement.EnumerateArray().First();

        Assert.Equal(
            new[] { "id", "listingCount", "nameAz", "nameRu", "slug" },
            first.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task The_admin_region_endpoint_is_role_gated()
    {
        using var factory = await SeededFactoryAsync();

        var anonymous = await factory.CreateApiClient().GetAsync("/api/v1/admin/regions");
        var user = await (await AuthenticatedClientAsync(factory, UserRole.User)).GetAsync("/api/v1/admin/regions");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, user.StatusCode);
    }

    [Fact]
    public async Task Region_import_is_admin_only_and_idempotent()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AuthenticatedClientAsync(factory, UserRole.Admin);

        var rows = new List<RegionImportRow> { Row("Ağdaş", true, sortOrder: 500), Row("Masallı", true, sortOrder: 510) };

        var anonymous = factory.CreateApiClient();
        var anonymousResponse = await anonymous.PostAsJsonAsync(
            "/api/v1/admin/regions/import", new RegionImportRequest(rows));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var first = await admin.PostAsJsonAsync("/api/v1/admin/regions/import", new RegionImportRequest(rows));
        var firstResult = (await first.Content.ReadFromJsonAsync<RegionImportResult>())!;

        var second = await admin.PostAsJsonAsync("/api/v1/admin/regions/import", new RegionImportRequest(rows));
        var secondResult = (await second.Content.ReadFromJsonAsync<RegionImportResult>())!;

        Assert.Equal(2, firstResult.Inserted);
        Assert.Equal(0, secondResult.Inserted);
        Assert.Equal(2, secondResult.Updated);

        var regions = (await admin.GetFromJsonAsync<List<RegionDto>>("/api/v1/regions"))!;
        Assert.Contains(regions, r => r.Slug == "agdas");
        Assert.Contains(regions, r => r.Slug == "masalli");
    }

    [Fact]
    public async Task Region_import_reports_bad_rows_without_aborting_the_batch()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AuthenticatedClientAsync(factory, UserRole.Admin);

        var rows = new List<RegionImportRow>
        {
            Row("Şirvan", true, sortOrder: 520),
            new(NameAz: "Naməlum", NameRu: null, Slug: null, Type: "NotAType", ParentSlug: null,
                IsSelectable: true, Latitude: null, Longitude: null, SortOrder: 530),
            Row("Yetim", true, parentSlug: "movcud-deyil", sortOrder: 540)
        };

        var response = await admin.PostAsJsonAsync("/api/v1/admin/regions/import", new RegionImportRequest(rows));
        var result = (await response.Content.ReadFromJsonAsync<RegionImportResult>())!;

        Assert.Equal(1, result.Inserted);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public async Task An_admin_mutation_writes_an_audit_entry()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AuthenticatedClientAsync(factory, UserRole.Admin);

        var fishing = (await admin.GetFromJsonAsync<CategoryDetailDto>("/api/v1/categories/baliqciliq"))!;
        await admin.PutAsJsonAsync($"/api/v1/admin/categories/{fishing.Id}/restriction",
            new SetRestrictionRequest("Restricted"));

        var audits = await factory.GetAuditActionsAsync();

        Assert.Contains("CategoryRestrictionChanged", audits);
    }

    [Fact]
    public async Task Deleting_a_category_that_has_children_is_refused()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AuthenticatedClientAsync(factory, UserRole.Admin);

        var fishing = (await admin.GetFromJsonAsync<CategoryDetailDto>("/api/v1/categories/baliqciliq"))!;

        var response = await admin.DeleteAsync($"/api/v1/admin/categories/{fishing.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Schema_payload_carries_everything_a_form_control_needs()
    {
        using var factory = await SeededFactoryAsync();
        var client = factory.CreateApiClient();

        var raw = await client.GetStringAsync("/api/v1/categories/tilovlar/schema");
        var length = JsonDocument.Parse(raw).RootElement
            .GetProperty("attributes").EnumerateArray()
            .Single(a => a.GetProperty("key").GetString() == "length");

        // Phase 4 must be able to render the control from this alone.
        Assert.Equal("Number", length.GetProperty("dataType").GetString());
        Assert.Equal("m", length.GetProperty("unit").GetString());
        Assert.True(length.GetProperty("isRequired").GetBoolean());
        Assert.Equal(0.5, length.GetProperty("minValue").GetDouble());
        Assert.Equal(6, length.GetProperty("maxValue").GetDouble());
        Assert.Equal(2, length.GetProperty("decimalPlaces").GetInt32());
        Assert.False(string.IsNullOrEmpty(length.GetProperty("placeholderAz").GetString()));
    }
}
