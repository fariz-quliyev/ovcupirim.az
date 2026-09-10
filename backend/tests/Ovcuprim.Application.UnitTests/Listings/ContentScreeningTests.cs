using Microsoft.Extensions.Options;
using Ovcuprim.Application.Listings;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Listings;

/// <summary>
/// The composite screener and the one policy that ships with content: duplicate detection. Every
/// case here proves the same fact from a different angle — a flag is information for a moderator,
/// never a decision. <c>ListingModerationTests</c> already covers the general "flagged publishes
/// still reach the queue" behaviour through <c>RecordingScreener</c>; these are about what actually
/// produces a flag.
/// </summary>
public class ContentScreeningTests
{
    private static DuplicateListingScreeningPolicy BuildPolicy(
        ListingTestHarness h, bool enabled = true, int lookbackDays = 30) =>
        new(h.Db, Options.Create(new ScreeningOptions
        {
            DuplicateDetectionEnabled = enabled,
            DuplicateLookbackDays = lookbackDays
        }), h.Clock);

    private static async Task<Guid> PendingAsync(ListingTestHarness h, string? category = null)
    {
        h.ActAsSeller();
        var id = await h.DraftWithImageAsync(category);
        await h.Publishing.PublishAsync(id, new PublishListingRequest(false));
        return id;
    }

    [Fact]
    public async Task The_composite_screener_pools_flags_from_every_policy()
    {
        using var h = await Seeded();

        var recorder = new RecordingScreeningPolicy("first", new ScreeningFlag("first", "Birinci."));
        var second = new RecordingScreeningPolicy("second", new ScreeningFlag("second", "İkinci."));
        var composite = new CompositeContentScreener([recorder, second]);

        var result = await composite.ScreenAsync(new ListingScreeningInput(
            Guid.NewGuid(), h.SellerId, h.LeafCategoryId, "Başlıq", "Təsvir", 100m));

        Assert.False(result.IsClean);
        Assert.Equal(2, result.Flags.Count);
        Assert.Contains(result.Flags, f => f.Code == "first");
        Assert.Contains(result.Flags, f => f.Code == "second");
    }

    [Fact]
    public async Task The_composite_screener_is_clean_when_no_policy_flags_anything()
    {
        using var h = await Seeded();

        var composite = new CompositeContentScreener([new RecordingScreeningPolicy("quiet")]);

        var result = await composite.ScreenAsync(new ListingScreeningInput(
            Guid.NewGuid(), h.SellerId, h.LeafCategoryId, "Başlıq", "Təsvir", 100m));

        Assert.True(result.IsClean);
    }

    [Fact]
    public async Task A_second_submission_with_the_same_title_in_the_same_category_is_flagged()
    {
        using var h = await Seeded();
        await PendingAsync(h);

        var policy = BuildPolicy(h);
        var flags = await policy.EvaluateAsync(new ListingScreeningInput(
            Guid.NewGuid(), h.SellerId, h.LeafCategoryId, h.NewListing().Title, "Başqa təsvir", 100m));

        var flag = Assert.Single(flags);
        Assert.Equal("duplicate", flag.Code);
    }

    [Fact]
    public async Task Folded_case_and_diacritics_still_count_as_the_same_title()
    {
        using var h = await Seeded();
        await PendingAsync(h);

        // The seeded title is "İkinəfərlik turist çadırı"; this asks about the same words folded to
        // plain ASCII and shouted in caps — the same normalisation the search key already uses.
        var policy = BuildPolicy(h);
        var flags = await policy.EvaluateAsync(new ListingScreeningInput(
            Guid.NewGuid(), h.SellerId, h.LeafCategoryId, "IKINEFERLIK TURIST CADIRI", "Başqa təsvir", 100m));

        Assert.Single(flags);
    }

    [Fact]
    public async Task Flagging_never_blocks_the_publish_it_flags()
    {
        using var h = await Seeded();
        await PendingAsync(h);

        var policy = BuildPolicy(h);
        h.Screener.Flags.Clear();

        // Wire the harness's own publish path to the real policy for one call, the way DI would.
        var realScreener = new CompositeContentScreener([policy]);
        var publishing = new ListingPublishService(
            h.Db, h.Categories, h.Regions, h.Validator, h.Quota, realScreener, h.CurrentUser, h.Storage, h.Clock);

        h.ActAsSeller();
        var id = await h.DraftWithImageAsync();
        var published = await publishing.PublishAsync(id, new PublishListingRequest(false));

        Assert.True(published.Succeeded);
        Assert.Equal(nameof(ListingStatus.PendingModeration), published.Value!.Status);
    }

    [Fact]
    public async Task A_different_seller_with_the_same_title_is_not_flagged()
    {
        using var h = await Seeded();
        await PendingAsync(h);

        h.ActAsOtherUser();
        var otherId = h.CurrentUser.UserId!.Value;

        var policy = BuildPolicy(h);
        var flags = await policy.EvaluateAsync(new ListingScreeningInput(
            Guid.NewGuid(), otherId, h.LeafCategoryId, h.NewListing().Title, "Başqa təsvir", 100m));

        Assert.Empty(flags);
    }

    [Fact]
    public async Task The_same_title_in_a_different_category_is_not_flagged()
    {
        using var h = await Seeded();
        await PendingAsync(h);

        var policy = BuildPolicy(h);
        var flags = await policy.EvaluateAsync(new ListingScreeningInput(
            Guid.NewGuid(), h.SellerId, h.RestrictedCategoryId, h.NewListing().Title, "Başqa təsvir", 100m));

        Assert.Empty(flags);
    }

    [Fact]
    public async Task Outside_the_lookback_window_the_earlier_submission_is_not_a_duplicate()
    {
        using var h = await Seeded();
        await PendingAsync(h);

        h.Clock.Advance(TimeSpan.FromDays(31));

        var policy = BuildPolicy(h, lookbackDays: 30);
        var flags = await policy.EvaluateAsync(new ListingScreeningInput(
            Guid.NewGuid(), h.SellerId, h.LeafCategoryId, h.NewListing().Title, "Başqa təsvir", 100m));

        Assert.Empty(flags);
    }

    [Fact]
    public async Task A_rejected_listing_is_not_a_candidate_for_duplicate_matching()
    {
        using var h = await Seeded();
        var id = await PendingAsync(h);

        h.ActAsModerator();
        await h.Moderation.RejectAsync(id, "Uyğun deyil.");

        // Rejected sits outside "live or awaiting a decision" — resubmitting the same listing is the
        // seller fixing what was rejected, not a duplicate of itself.
        var policy = BuildPolicy(h);
        var flags = await policy.EvaluateAsync(new ListingScreeningInput(
            Guid.NewGuid(), h.SellerId, h.LeafCategoryId, h.NewListing().Title, "Başqa təsvir", 100m));

        Assert.Empty(flags);
    }

    [Fact]
    public async Task Comparing_a_listing_against_itself_is_not_a_duplicate()
    {
        using var h = await Seeded();
        var id = await PendingAsync(h);

        var policy = BuildPolicy(h);
        var flags = await policy.EvaluateAsync(new ListingScreeningInput(
            id, h.SellerId, h.LeafCategoryId, h.NewListing().Title, "Başqa təsvir", 100m));

        Assert.Empty(flags);
    }

    [Fact]
    public async Task Disabling_duplicate_detection_flags_nothing()
    {
        using var h = await Seeded();
        await PendingAsync(h);

        var policy = BuildPolicy(h, enabled: false);
        var flags = await policy.EvaluateAsync(new ListingScreeningInput(
            Guid.NewGuid(), h.SellerId, h.LeafCategoryId, h.NewListing().Title, "Başqa təsvir", 100m));

        Assert.Empty(flags);
    }

    private static async Task<ListingTestHarness> Seeded()
    {
        var h = new ListingTestHarness();
        await h.SeedAsync();
        return h;
    }

    /// <summary>Always returns the same fixed flag, so the composite screener's pooling is what is under test.</summary>
    private sealed class RecordingScreeningPolicy(string code, params ScreeningFlag[] flags) : IScreeningPolicy
    {
        public string Code => code;

        public Task<IReadOnlyList<ScreeningFlag>> EvaluateAsync(
            ListingScreeningInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScreeningFlag>>(flags);
    }
}
