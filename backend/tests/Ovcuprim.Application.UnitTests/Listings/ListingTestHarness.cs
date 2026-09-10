using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Categories;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Listings.Search;
using Ovcuprim.Application.Notifications;
using Ovcuprim.Application.Regions;
using Ovcuprim.Application.UnitTests.Auth;
using Ovcuprim.Application.UnitTests.Catalog;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;
using Ovcuprim.Infrastructure.Persistence;

namespace Ovcuprim.Application.UnitTests.Listings;

/// <summary>Records what was written without touching the disk.</summary>
public sealed class FakeFileStorage : IFileStorage
{
    public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);

    public List<string> Deleted { get; } = [];

    public Task<string> SaveAsync(Stream content, string key, string contentType, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        Objects[key] = buffer.ToArray();
        return Task.FromResult(key);
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(Objects.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Deleted.Add(key);
        Objects.Remove(key);
        return Task.CompletedTask;
    }

    public string GetPublicUrl(string key) => $"/uploads/{key}";

    /// <summary>Written timestamps the tests control, so the age guard can be exercised.</summary>
    public Dictionary<string, DateTimeOffset> WrittenAt { get; } = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<string>> ListKeysAsync(
        string prefix, DateTimeOffset modifiedBefore, CancellationToken cancellationToken = default)
    {
        var keys = Objects.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .Where(key => WrittenAt.GetValueOrDefault(key, DateTimeOffset.MinValue) < modifiedBefore)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(keys);
    }
}

/// <summary>Stands in for ImageSharp so the service tests stay about the rules, not about codecs.</summary>
public sealed class FakeImageProcessor : IImageProcessor
{
    public ImageProcessingFailure NextFailure { get; set; } = ImageProcessingFailure.None;

    /// <summary>Proves a rejection earlier in the pipeline (declared content type, empty file) never reaches here.</summary>
    public int Calls { get; private set; }

    public Task<ImageProcessingResult> ProcessAsync(Stream input, CancellationToken cancellationToken = default)
    {
        Calls++;

        if (NextFailure != ImageProcessingFailure.None)
        {
            return Task.FromResult(ImageProcessingResult.Fail(NextFailure));
        }

        var master = new ImageRendition("master", [1, 2, 3, 4], 1600, 1200);

        return Task.FromResult(ImageProcessingResult.Ok(new ProcessedImageSet(
            1600, 1200, "image/webp", ".webp", master,
            [
                new ImageRendition("thumb", [1], 160, 160),
                new ImageRendition("card", [1], 400, 300),
                new ImageRendition("detail", [1], 1280, 960),
                new ImageRendition("og", [1], 1200, 630)
            ])));
    }
}

/// <summary>Returns whatever a test asks it to, so the quota rules can be exercised without configuration.</summary>
public sealed class StubQuotaPolicy : IListingQuotaPolicy
{
    public int? Default { get; set; }

    public Dictionary<int, int?> PerCategory { get; } = [];

    public int? LimitFor(int categoryId) =>
        PerCategory.TryGetValue(categoryId, out var limit) ? limit : Default;
}

public sealed class RecordingScreener : IContentScreener
{
    public List<ScreeningFlag> Flags { get; } = [];

    public int Calls { get; private set; }

    public Task<ScreeningResult> ScreenAsync(ListingScreeningInput input, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new ScreeningResult(Flags));
    }
}

/// <summary>
/// Stands in for the PostgreSQL-backed search store. The catalogue SQL cannot run on the in-memory
/// provider, so the query composition is proven by the parser tests and against a real database;
/// this stub only lets the services that merely pass through it be exercised.
/// </summary>
public sealed class StubListingSearchStore : IListingSearchStore
{
    public Task<ListingIdPage> SearchAsync(ListingQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Catalogue SQL requires PostgreSQL.");

    public Task<ListingFacets> FacetsAsync(ListingQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Facet SQL requires PostgreSQL.");

    public Task<int> RefreshListingCountsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Dictionary<Guid, int> AppliedViews { get; } = [];

    public Task<int> ApplyViewCountsAsync(
        IReadOnlyDictionary<Guid, int> increments, CancellationToken cancellationToken = default)
    {
        foreach (var (id, delta) in increments)
        {
            AppliedViews[id] = AppliedViews.GetValueOrDefault(id) + delta;
        }

        return Task.FromResult(increments.Count);
    }
}

public sealed class ListingTestHarness : IDisposable
{
    public ListingTestHarness()
    {
        DatabaseName = $"listings-{Guid.NewGuid()}";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(DatabaseName)
            .Options;

        Clock = new FakeClock(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        Db = new AppDbContext(options, Clock);
        Cache = new NoOpTaxonomyCache();
        CurrentUser = new StubCurrentUser { Role = "User" };
        Storage = new FakeFileStorage();
        ImageProcessor = new FakeImageProcessor();
        QuotaPolicy = new StubQuotaPolicy();
        Screener = new RecordingScreener();

        Categories = new CategoryService(Db, Cache);
        Regions = new RegionService(Db, Cache, Clock);
        Validator = new AttributeValidator();
        Quota = new ListingQuotaService(Db, QuotaPolicy, Clock);

        ViewCounts = new ViewCountBuffer();
        Listings = new ListingService(Db, Categories, Regions, Validator, CurrentUser, Storage, ViewCounts, Clock);
        Publishing = new ListingPublishService(
            Db, Categories, Regions, Validator, Quota, Screener, CurrentUser, Storage, Clock);
        Media = new ListingMediaService(Db, Storage, ImageProcessor, CurrentUser, Clock);
        Notifications = new NotificationService(Db, [new InAppNotificationChannel(Db, Clock)], CurrentUser, Clock);
        Moderation = new ListingModerationService(Db, Categories, CurrentUser, Storage, Clock, Notifications);

        SearchStore = new StubListingSearchStore();
        Parser = new ListingQueryParser(Db, Categories);
        Search = new ListingSearchService(Db, SearchStore, Parser, Categories, Regions, CurrentUser, Storage);
        Favorites = new FavoriteService(Db, Search, CurrentUser, Clock);
        Reports = new ListingReportService(Db, CurrentUser, Clock);
    }

    public AppDbContext Db { get; }

    private string DatabaseName { get; } = null!;

    /// <summary>
    /// A second context over the same store, for the interleavings a concurrency token exists to
    /// catch: one caller reads, another writes, the first saves.
    /// </summary>
    public AppDbContext NewContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(DatabaseName).Options,
        Clock);
    public FakeClock Clock { get; }
    public NoOpTaxonomyCache Cache { get; }
    public StubCurrentUser CurrentUser { get; }
    public FakeFileStorage Storage { get; }
    public ViewCountBuffer ViewCounts { get; } = null!;
    public FakeImageProcessor ImageProcessor { get; }
    public StubQuotaPolicy QuotaPolicy { get; }
    public RecordingScreener Screener { get; }
    public ICategoryService Categories { get; }
    public IRegionService Regions { get; }
    public IAttributeValidator Validator { get; }
    public IListingQuotaService Quota { get; }
    public IListingService Listings { get; }
    public IListingPublishService Publishing { get; }
    public IListingMediaService Media { get; }
    public IListingModerationService Moderation { get; }
    public INotificationService Notifications { get; } = null!;
    public StubListingSearchStore SearchStore { get; } = null!;
    public IListingQueryParser Parser { get; } = null!;
    public IListingSearchService Search { get; } = null!;
    public IFavoriteService Favorites { get; } = null!;
    public IListingReportService Reports { get; } = null!;

    public Guid SellerId { get; private set; }
    public Guid ModeratorId { get; private set; }
    public int LeafCategoryId { get; private set; }
    public int RestrictedCategoryId { get; private set; }
    public string LeafSlug => "cadirlar";
    public string RestrictedSlug => "restricted-leaf";
    public string RegionSlug => "baki";

    /// <summary>
    /// A minimal but realistic world: one seller, one moderator, a selectable region, an
    /// unrestricted leaf with one required attribute, and a restricted leaf under a parent.
    /// </summary>
    public async Task SeedAsync()
    {
        var seller = new User
        {
            Id = Guid.CreateVersion7(), PhoneNumber = "+994501112233", IsPhoneVerified = true,
            FullName = "Test Satıcı", Role = UserRole.User, Status = UserStatus.Active, CreatedAt = Clock.UtcNow
        };

        var moderator = new User
        {
            Id = Guid.CreateVersion7(), PhoneNumber = "+994502223344", IsPhoneVerified = true,
            FullName = "Test Moderator", Role = UserRole.Moderator, Status = UserStatus.Active, CreatedAt = Clock.UtcNow
        };

        Db.Users.AddRange(seller, moderator);

        Db.Regions.Add(new Region
        {
            Slug = RegionSlug, NameAz = "Bakı", Type = RegionType.City,
            IsActive = true, IsSelectable = true, SortOrder = 10, CreatedAt = Clock.UtcNow
        });

        Db.Regions.Add(new Region
        {
            Slug = "nesimi", NameAz = "Nəsimi", Type = RegionType.District,
            IsActive = true, IsSelectable = false, SortOrder = 20, CreatedAt = Clock.UtcNow
        });

        var parent = new Category
        {
            Slug = "kamp", NameAz = "Kamp", Depth = 0, SortOrder = 10, IsActive = true,
            RestrictionStatus = RestrictionStatus.Unrestricted, CreatedAt = Clock.UtcNow
        };

        Db.Categories.Add(parent);
        await Db.SaveChangesAsync();

        var leaf = new Category
        {
            Slug = LeafSlug, NameAz = "Çadırlar", ParentId = parent.Id, Depth = 1, SortOrder = 10, IsActive = true,
            RestrictionStatus = RestrictionStatus.Unrestricted, CreatedAt = Clock.UtcNow
        };

        var restricted = new Category
        {
            Slug = RestrictedSlug, NameAz = "Məhdud", ParentId = parent.Id, Depth = 1, SortOrder = 20, IsActive = true,
            RestrictionStatus = RestrictionStatus.Restricted, CreatedAt = Clock.UtcNow
        };

        Db.Categories.AddRange(leaf, restricted);
        await Db.SaveChangesAsync();

        var capacity = new AttributeDefinition
        {
            CategoryId = leaf.Id, Key = "capacity_person", LabelAz = "Tutum", DataType = AttributeDataType.Select,
            IsRequired = true, IsActive = true, SortOrder = 10, CreatedAt = Clock.UtcNow
        };

        capacity.Options.Add(new AttributeOption { Value = "2", LabelAz = "2 nəfər", SortOrder = 10, IsActive = true });
        capacity.Options.Add(new AttributeOption { Value = "4", LabelAz = "4 nəfər", SortOrder = 20, IsActive = true });

        Db.AttributeDefinitions.Add(capacity);

        Db.AttributeDefinitions.Add(new AttributeDefinition
        {
            CategoryId = leaf.Id, Key = "weight", LabelAz = "Çəki", DataType = AttributeDataType.Number,
            Unit = "kq", MinValue = 0, MaxValue = 100, DecimalPlaces = 2, IsActive = true,
            SortOrder = 20, CreatedAt = Clock.UtcNow
        });

        await Db.SaveChangesAsync();

        SellerId = seller.Id;
        ModeratorId = moderator.Id;
        LeafCategoryId = leaf.Id;
        RestrictedCategoryId = restricted.Id;

        CurrentUser.UserId = seller.Id;
        CurrentUser.Role = "User";
    }

    public void ActAsSeller()
    {
        CurrentUser.UserId = SellerId;
        CurrentUser.Role = "User";
    }

    public void ActAsModerator()
    {
        CurrentUser.UserId = ModeratorId;
        CurrentUser.Role = "Moderator";
    }

    public void ActAsOtherUser()
    {
        CurrentUser.UserId = Guid.CreateVersion7();
        CurrentUser.Role = "User";
    }

    public CreateListingRequest NewListing(
        string? categorySlug = null,
        decimal? price = 120m,
        Dictionary<string, System.Text.Json.JsonElement>? attributes = null) =>
        new(
            categorySlug ?? LeafSlug,
            RegionSlug,
            "İkinəfərlik turist çadırı",
            "Yaxşı vəziyyətdə, az istifadə olunub.",
            price,
            "Used",
            "Naturehike",
            true,
            "0501112233",
            true,
            // Only the tent leaf declares attributes; other categories have an empty schema.
            attributes ?? (categorySlug is null || categorySlug == LeafSlug
                ? Attributes(("capacity_person", "\"2\""))
                : Attributes()));

    public static Dictionary<string, System.Text.Json.JsonElement> Attributes(params (string Key, string Json)[] values)
    {
        var result = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);

        foreach (var (key, json) in values)
        {
            result[key] = System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
        }

        return result;
    }

    /// <summary>Creates a draft, attaches one image and returns it ready to publish.</summary>
    public async Task<Guid> DraftWithImageAsync(string? categorySlug = null)
    {
        var created = await Listings.CreateDraftAsync(NewListing(categorySlug));
        var id = created.Value!.Id;

        await Media.AddAsync(id, Upload());

        return id;
    }

    public static ListingMediaUpload Upload(string contentType = "image/jpeg", long? length = null)
    {
        // A real JPEG signature: the service sniffs the bytes, not the declared type.
        byte[] bytes = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        return new ListingMediaUpload(new MemoryStream(bytes), "photo.jpg", contentType, length ?? bytes.Length);
    }

    public Task<Listing> ReloadAsync(Guid id) =>
        Db.Listings.AsNoTracking().Include(l => l.Media).FirstAsync(l => l.Id == id);

    public void Dispose() => Db.Dispose();
}
