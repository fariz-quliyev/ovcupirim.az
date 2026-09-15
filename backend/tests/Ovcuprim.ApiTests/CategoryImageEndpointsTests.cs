using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ovcuprim.Application.Categories;
using Ovcuprim.Domain.Enums;
using Ovcuprim.Infrastructure.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ovcuprim.Api.IntegrationTests;

/// <summary>
/// The picture on a category tile. Before this existed an administrator could only type the key of
/// a file somebody had already put on the server by hand, which is to say they could not change it.
/// </summary>
public class CategoryImageEndpointsTests
{
    private const string Phone = "+994503334455";

    private static async Task<ApiFactory> SeededFactoryAsync()
    {
        var factory = new ApiFactory();
        await factory.SeedTaxonomyAsync();

        return factory;
    }

    private static async Task<HttpClient> AdminAsync(ApiFactory factory)
    {
        var client = factory.CreateApiClient();

        await client.PostAsJsonAsync("/api/v1/auth/register", new Application.Auth.RegisterRequest(Phone, "Admin"));
        await client.PostAsJsonAsync("/api/v1/auth/verify",
            new Application.Auth.VerifyOtpRequest(Phone, factory.Sms.LastCodeFor(Phone), OtpPurpose.Registration));

        await factory.SetRoleAsync(Phone, UserRole.Admin);

        return await factory.SignInAsync(Phone, UserRole.Admin);
    }

    private static MultipartFormDataContent ImageUpload(int width = 640, int height = 480, string name = "ovculuq.jpg")
    {
        using var image = new Image<Rgba32>(width, height);
        using var buffer = new MemoryStream();
        image.SaveAsJpeg(buffer);

        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(buffer.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", name);

        return content;
    }

    private static async Task<int> CategoryIdAsync(ApiFactory factory, string slug)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstAsync(db.Categories, c => c.Slug == slug)).Id;
    }

    [Fact]
    public async Task An_upload_stores_the_picture_and_puts_its_key_on_the_category()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AdminAsync(factory);
        var id = await CategoryIdAsync(factory, "ovculuq");

        var response = await admin.PostAsync($"/api/v1/admin/categories/{id}/image", ImageUpload());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var category = (await response.Content.ReadFromJsonAsync<CategoryDetailDto>())!;

        Assert.NotNull(category.ImageKey);
        Assert.StartsWith("categories/", category.ImageKey, StringComparison.Ordinal);
        Assert.Contains(category.ImageKey, factory.Storage.Objects.Keys);
    }

    [Fact]
    public async Task The_key_is_the_servers_own_and_never_the_uploaded_filename()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AdminAsync(factory);
        var id = await CategoryIdAsync(factory, "ovculuq");

        var response = await admin.PostAsync(
            $"/api/v1/admin/categories/{id}/image", ImageUpload(name: "../../evil.php"));

        var category = (await response.Content.ReadFromJsonAsync<CategoryDetailDto>())!;

        Assert.DoesNotContain("evil", category.ImageKey!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("..", category.ImageKey!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replacing_a_picture_deletes_the_one_it_replaced()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AdminAsync(factory);
        var id = await CategoryIdAsync(factory, "ovculuq");

        var first = (await (await admin.PostAsync($"/api/v1/admin/categories/{id}/image", ImageUpload()))
            .Content.ReadFromJsonAsync<CategoryDetailDto>())!;

        // Different dimensions, so the bytes differ and the hash-derived key differs with them.
        var second = (await (await admin.PostAsync($"/api/v1/admin/categories/{id}/image", ImageUpload(320, 240)))
            .Content.ReadFromJsonAsync<CategoryDetailDto>())!;

        Assert.NotEqual(first.ImageKey, second.ImageKey);
        Assert.Contains(first.ImageKey!, factory.Storage.Deleted);
        Assert.DoesNotContain(first.ImageKey!, factory.Storage.Objects.Keys);
        Assert.Contains(second.ImageKey!, factory.Storage.Objects.Keys);
    }

    [Fact]
    public async Task The_same_picture_uploaded_twice_keeps_its_key_and_its_file()
    {
        // The key is a hash of the stored bytes, so this is the one case where the replaced file
        // and the replacement are the same object — deleting the "old" one would delete the new.
        using var factory = await SeededFactoryAsync();
        var admin = await AdminAsync(factory);
        var id = await CategoryIdAsync(factory, "ovculuq");

        var first = (await (await admin.PostAsync($"/api/v1/admin/categories/{id}/image", ImageUpload()))
            .Content.ReadFromJsonAsync<CategoryDetailDto>())!;

        var again = (await (await admin.PostAsync($"/api/v1/admin/categories/{id}/image", ImageUpload()))
            .Content.ReadFromJsonAsync<CategoryDetailDto>())!;

        Assert.Equal(first.ImageKey, again.ImageKey);
        Assert.Contains(again.ImageKey!, factory.Storage.Objects.Keys);
        Assert.DoesNotContain(again.ImageKey!, factory.Storage.Deleted);
    }

    [Fact]
    public async Task Removing_the_picture_clears_the_key_and_deletes_the_file()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AdminAsync(factory);
        var id = await CategoryIdAsync(factory, "ovculuq");

        var uploaded = (await (await admin.PostAsync($"/api/v1/admin/categories/{id}/image", ImageUpload()))
            .Content.ReadFromJsonAsync<CategoryDetailDto>())!;

        var response = await admin.DeleteAsync($"/api/v1/admin/categories/{id}/image");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var category = (await response.Content.ReadFromJsonAsync<CategoryDetailDto>())!;

        Assert.Null(category.ImageKey);
        Assert.Contains(uploaded.ImageKey!, factory.Storage.Deleted);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AdminAsync(factory);
        var id = await CategoryIdAsync(factory, "ovculuq");

        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent("not an image at all"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "picture.jpg");

        var response = await admin.PostAsync($"/api/v1/admin/categories/{id}/image", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Storage.Objects);
    }

    [Fact]
    public async Task An_ordinary_user_cannot_change_a_category_picture()
    {
        using var factory = await SeededFactoryAsync();
        var id = await CategoryIdAsync(factory, "ovculuq");

        var client = factory.CreateApiClient();
        await client.PostAsJsonAsync("/api/v1/auth/register", new Application.Auth.RegisterRequest("+994505556677", "Adi"));
        await client.PostAsJsonAsync("/api/v1/auth/verify", new Application.Auth.VerifyOtpRequest(
            "+994505556677", factory.Sms.LastCodeFor("+994505556677"), OtpPurpose.Registration));

        var user = await factory.SignInAsync("+994505556677", UserRole.User);

        var response = await user.PostAsync($"/api/v1/admin/categories/{id}/image", ImageUpload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Storage.Objects);
    }

    [Fact]
    public async Task An_unknown_category_is_a_404_and_writes_nothing()
    {
        using var factory = await SeededFactoryAsync();
        var admin = await AdminAsync(factory);

        var response = await admin.PostAsync("/api/v1/admin/categories/999999/image", ImageUpload());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Storage.Objects);
    }

    [Fact]
    public async Task The_public_tree_shows_the_new_picture_without_waiting_for_the_cache_to_lapse()
    {
        // The taxonomy is held in process for an hour. A picture that only appeared after a restart
        // would be indistinguishable from one that never saved.
        using var factory = await SeededFactoryAsync();
        var admin = await AdminAsync(factory);
        var id = await CategoryIdAsync(factory, "ovculuq");

        var anonymous = factory.CreateApiClient();

        // Warm the cache first, so this proves invalidation rather than a cold read.
        await anonymous.GetAsync("/api/v1/categories");

        var uploaded = (await (await admin.PostAsync($"/api/v1/admin/categories/{id}/image", ImageUpload()))
            .Content.ReadFromJsonAsync<CategoryDetailDto>())!;

        var tree = await anonymous.GetStringAsync("/api/v1/categories");

        Assert.Contains(uploaded.ImageKey!, tree, StringComparison.Ordinal);
    }
}
