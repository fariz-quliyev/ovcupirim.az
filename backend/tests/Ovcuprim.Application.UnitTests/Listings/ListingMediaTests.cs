using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;

namespace Ovcuprim.Application.UnitTests.Listings;

/// <summary>
/// Uploads are the most abusable surface on the API, so the checks here are about what the bytes
/// are, how many there may be, and keeping the unique sort-order index satisfied while reordering.
/// </summary>
public class ListingMediaTests
{
    private static async Task<(ListingTestHarness Harness, Guid ListingId)> BuildAsync()
    {
        var harness = new ListingTestHarness();
        await harness.SeedAsync();
        var created = await harness.Listings.CreateDraftAsync(harness.NewListing());
        return (harness, created.Value!.Id);
    }

    [Fact]
    public async Task The_first_image_becomes_the_cover()
    {
        var (h, id) = await BuildAsync();
        using var _ = h;

        var added = await h.Media.AddAsync(id, ListingTestHarness.Upload());

        Assert.True(added.Succeeded);
        Assert.True(added.Value!.IsPrimary);
        Assert.Equal(0, added.Value.SortOrder);
    }

    [Fact]
    public async Task Every_variant_is_written_to_storage()
    {
        var (h, id) = await BuildAsync();
        using var _ = h;

        var added = await h.Media.AddAsync(id, ListingTestHarness.Upload());

        Assert.Equal(5, h.Storage.Objects.Count);
        Assert.Equal(["card", "detail", "og", "thumb"], added.Value!.Variants.Keys.OrderBy(k => k).ToArray());
    }

    [Fact]
    public async Task The_stored_key_never_contains_the_uploaded_filename()
    {
        var (h, id) = await BuildAsync();
        using var _ = h;

        var upload = new ListingMediaUpload(
            ListingTestHarness.Upload().Content, "../../evil name.jpg", "image/jpeg", 32);

        await h.Media.AddAsync(id, upload);

        Assert.All(h.Storage.Objects.Keys, key =>
        {
            Assert.DoesNotContain("evil", key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("..", key, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_rejected_on_its_bytes()
    {
        var (h, id) = await BuildAsync();
        using var _ = h;

        // Declares itself a JPEG, but the bytes are text.
        var bytes = "MZ this is not an image at all, definitely not"u8.ToArray();
        var upload = new ListingMediaUpload(new MemoryStream(bytes), "photo.jpg", "image/jpeg", bytes.Length);

        var added = await h.Media.AddAsync(id, upload);

        Assert.False(added.Succeeded);
        Assert.True(added.FieldErrors!.ContainsKey("file"));
        Assert.Empty(h.Storage.Objects);
    }

    [Fact]
    public async Task An_unsupported_declared_type_is_rejected()
    {
        var (h, id) = await BuildAsync();
        using var _ = h;

        var added = await h.Media.AddAsync(id, ListingTestHarness.Upload(contentType: "application/pdf"));

        Assert.False(added.Succeeded);
        Assert.True(added.FieldErrors!.ContainsKey("file"));
    }

    [Fact]
    public async Task A_file_over_five_megabytes_is_rejected()
    {
        var (h, id) = await BuildAsync();
        using var _ = h;

        var added = await h.Media.AddAsync(id, ListingTestHarness.Upload(length: 5 * 1024 * 1024 + 1));

        Assert.False(added.Succeeded);
        Assert.Contains("5 MB", added.FieldErrors!["file"][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_eleventh_image_is_refused()
    {
        var (h, id) = await BuildAsync();
        using var _ = h;

        for (var i = 0; i < 10; i++)
        {
            Assert.True((await h.Media.AddAsync(id, ListingTestHarness.Upload())).Succeeded);
        }

        var eleventh = await h.Media.AddAsync(id, ListingTestHarness.Upload());

        Assert.False(eleventh.Succeeded);
        Assert.Contains("10", eleventh.FieldErrors!["file"][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Heic_is_reported_distinctly_from_a_corrupt_file()
    {
        var (h, id) = await BuildAsync();
        using var _ = h;

        h.ImageProcessor.NextFailure = ImageProcessingFailure.UnsupportedFormat;

        var added = await h.Media.AddAsync(id, ListingTestHarness.Upload());

        Assert.False(added.Succeeded);
        Assert.Contains("HEIC", added.FieldErrors!["file"][0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("image/heic")]
    [InlineData("image/heif")]
    public async Task A_declared_HEIC_upload_is_refused_at_the_door_not_after_processing(string contentType)
    {
        // B-3: HEIC is off the accepted contract entirely now, so a client that is honest about the
        // format never reaches image processing at all — the declared-type check answers first.
        var (h, id) = await BuildAsync();
        using var _ = h;

        var added = await h.Media.AddAsync(id, ListingTestHarness.Upload(contentType: contentType));

        Assert.False(added.Succeeded);
        Assert.Contains("JPEG", added.FieldErrors!["file"][0], StringComparison.Ordinal);
        Assert.Equal(0, h.ImageProcessor.Calls);
    }

    [Fact]
    public async Task Deleting_the_cover_promotes_the_next_image()
    {
        var (h, id) = await BuildAsync();
        using var _ = h;

        var first = await h.Media.AddAsync(id, ListingTestHarness.Upload());
        var second = await h.Media.AddAsync(id, ListingTestHarness.Upload());

        await h.Media.RemoveAsync(id, first.Value!.Id);

        var listing = await h.ReloadAsync(id);
        var remaining = Assert.Single(listing.Media);

        Assert.Equal(second.Value!.Id, remaining.Id);
        Assert.True(remaining.IsPrimary);
    }

    [Fact]
    public async Task Deleting_an_image_removes_every_stored_variant()
    {
        var (h, id) = await BuildAsync();
        using var _ = h;

        var added = await h.Media.AddAsync(id, ListingTestHarness.Upload());
        await h.Media.RemoveAsync(id, added.Value!.Id);

        Assert.Equal(5, h.Storage.Deleted.Count);
        Assert.Empty(h.Storage.Objects);
    }

    [Fact]
    public async Task Reordering_reverses_the_order_and_moves_the_cover()
    {
        var (h, id) = await BuildAsync();
        using var _ = h;

        var a = (await h.Media.AddAsync(id, ListingTestHarness.Upload())).Value!;
        var b = (await h.Media.AddAsync(id, ListingTestHarness.Upload())).Value!;
        var c = (await h.Media.AddAsync(id, ListingTestHarness.Upload())).Value!;

        var reordered = await h.Media.ReorderAsync(id, [c.Id, b.Id, a.Id]);

        Assert.True(reordered.Succeeded);

        var ordered = reordered.Value!;

        Assert.Equal([c.Id, b.Id, a.Id], ordered.Select(m => m.Id).ToArray());
        Assert.Equal([0, 1, 2], ordered.Select(m => m.SortOrder).ToArray());
        Assert.True(ordered[0].IsPrimary);
        Assert.Single(ordered, m => m.IsPrimary);
    }

    [Fact]
    public async Task A_partial_ordering_is_refused()
    {
        var (h, id) = await BuildAsync();
        using var _ = h;

        var a = (await h.Media.AddAsync(id, ListingTestHarness.Upload())).Value!;
        await h.Media.AddAsync(id, ListingTestHarness.Upload());

        var reordered = await h.Media.ReorderAsync(id, [a.Id]);

        Assert.False(reordered.Succeeded);
        Assert.True(reordered.FieldErrors!.ContainsKey("mediaIds"));
    }

    [Fact]
    public async Task Another_user_cannot_upload_to_someone_elses_listing()
    {
        var (h, id) = await BuildAsync();
        using var _ = h;

        h.ActAsOtherUser();
        var added = await h.Media.AddAsync(id, ListingTestHarness.Upload());

        Assert.Equal(ResultError.NotFound, added.Error);
    }

    [Fact]
    public async Task Images_cannot_be_changed_once_the_listing_is_sold()
    {
        var (h, _) = await BuildAsync();
        using var _h = h;

        var id = await ListingLifecycleTests.ActiveListingAsync(h);
        h.ActAsSeller();
        await h.Listings.MarkSoldAsync(id);

        var added = await h.Media.AddAsync(id, ListingTestHarness.Upload());

        Assert.False(added.Succeeded);
        Assert.Equal(ResultError.Conflict, added.Error);
    }
}
