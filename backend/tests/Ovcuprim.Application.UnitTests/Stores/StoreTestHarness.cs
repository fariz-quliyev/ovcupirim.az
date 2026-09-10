using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Stores;
using Ovcuprim.Application.UnitTests.Listings;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Stores;

/// <summary>
/// Builds on the listing harness, because almost every storefront question is really a question
/// about the listings filed under it.
/// </summary>
public sealed class StoreTestHarness : IDisposable
{
    private readonly ListingTestHarness _listings = new();

    public StoreTestHarness()
    {
        Stores = new StoreService(Db, CurrentUser, _listings.Categories, Storage, Clock);
        Admin = new StoreAdminService(Db, CurrentUser, Clock);
        Media = new StoreMediaService(Db, Storage, _listings.ImageProcessor, CurrentUser);
        Follows = new StoreFollowService(Db, CurrentUser, Storage, Clock);
    }

    public Infrastructure.Persistence.AppDbContext Db => _listings.Db;
    public Auth.FakeClock Clock => _listings.Clock;
    public Catalog.StubCurrentUser CurrentUser => _listings.CurrentUser;
    public FakeFileStorage Storage => _listings.Storage;
    public FakeImageProcessor ImageProcessor => _listings.ImageProcessor;

    public IStoreService Stores { get; }
    public IStoreAdminService Admin { get; }
    public IStoreMediaService Media { get; }
    public IStoreFollowService Follows { get; }

    public IListingService Listings => _listings.Listings;
    public IListingPublishService Publishing => _listings.Publishing;
    public IListingMediaService ListingMedia => _listings.Media;
    public IListingModerationService Moderation => _listings.Moderation;

    public Guid SellerId => _listings.SellerId;
    public Guid ModeratorId => _listings.ModeratorId;
    public string LeafSlug => _listings.LeafSlug;
    public string RestrictedSlug => _listings.RestrictedSlug;
    public string RegionSlug => _listings.RegionSlug;

    /// <summary>The root the two leaves hang off, for the directory's subtree filter.</summary>
    public string RootSlug => "kamp";

    public int LeafCategoryId => _listings.LeafCategoryId;
    public int RestrictedCategoryId => _listings.RestrictedCategoryId;

    public Task SeedAsync() => _listings.SeedAsync();

    public void ActAsSeller() => _listings.ActAsSeller();

    public void ActAsModerator() => _listings.ActAsModerator();

    public void ActAsOtherUser() => _listings.ActAsOtherUser();

    /// <summary>The admin policy is enforced at the API; the service only needs an actor.</summary>
    public void ActAsAdmin()
    {
        CurrentUser.UserId = ModeratorId;
        CurrentUser.Role = "Admin";
    }

    public CreateListingRequest NewListing(bool useStore = false) =>
        _listings.NewListing() with { UseStore = useStore };

    /// <summary>A second context over the same store, for interleaved-write tests.</summary>
    public Infrastructure.Persistence.AppDbContext NewContext() => _listings.NewContext();

    public Task<Listing> ReloadAsync(Guid id) => _listings.ReloadAsync(id);

    public static ListingMediaUpload Upload(string contentType = "image/jpeg", long? length = null) =>
        ListingTestHarness.Upload(contentType, length);

    public static ApplyForStoreRequest Application(string name = "Ovçu Dünyası") =>
        new(name, "Ov və kamp avadanlıqları.", "Bakı ş., Nəsimi r.", "0501112233");

    /// <summary>Applies as the seller and approves it, leaving an active storefront.</summary>
    public async Task<StoreOwnerDto> ActiveStoreAsync(string name = "Ovçu Dünyası")
    {
        ActAsSeller();
        var applied = await Stores.ApplyAsync(Application(name));

        ActAsAdmin();
        await Admin.ApproveAsync(applied.Value!.Id);

        ActAsSeller();

        return (await Stores.GetMineAsync()).Value!;
    }

    /// <summary>A live listing filed under the seller's storefront.</summary>
    public async Task<Guid> StoreListingAsync()
    {
        ActAsSeller();

        var created = await Listings.CreateDraftAsync(NewListing(useStore: true));
        var id = created.Value!.Id;

        await ListingMedia.AddAsync(id, Upload());
        await Publishing.PublishAsync(id, new PublishListingRequest(false));

        ActAsModerator();
        await Moderation.ApproveAsync(id);
        ActAsSeller();

        return id;
    }

    public void Dispose() => _listings.Dispose();
}
