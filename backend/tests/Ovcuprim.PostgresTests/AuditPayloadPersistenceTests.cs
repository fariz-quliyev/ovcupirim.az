using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Categories;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Notifications;
using Ovcuprim.Application.Stores;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;
using Ovcuprim.Infrastructure.Persistence;

namespace Ovcuprim.PostgresTests;

internal sealed class StubCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }

    public string? Role { get; set; } = "Admin";

    public bool IsAuthenticated => UserId is not null;

    public bool IsInRole(string role) => string.Equals(Role, role, StringComparison.OrdinalIgnoreCase);
}

/// <summary>These tests are about the audit trail, not about media, so storage does nothing.</summary>
internal sealed class NoOpFileStorage : IFileStorage
{
    public Task<string> SaveAsync(Stream content, string key, string contentType, CancellationToken cancellationToken = default) =>
        Task.FromResult(key);

    public Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public string GetPublicUrl(string key) => $"/uploads/{key}";

    public Task<IReadOnlyList<string>> ListKeysAsync(
        string prefix, DateTimeOffset modifiedBefore, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}

internal sealed class PassthroughTaxonomyCache : ITaxonomyCache
{
    public Task<T> GetOrCreateAsync<T>(
        string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default)
        where T : class => factory(cancellationToken);

    public void Invalidate()
    {
    }
}

/// <summary>
/// G1: the moderation decisions that write an audit row, run against the real database.
/// </summary>
/// <remarks>
/// Every one of these failed on PostgreSQL before the fix — <c>PayloadJson</c> is <c>jsonb</c> and
/// three services wrote plain text into it — while passing on the in-memory provider, which stores
/// the column as text. The audit row and the decision share one transaction, so a rejected payload
/// took the whole decision down with it.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class AuditPayloadPersistenceTests(PostgresFixture fixture)
{
    private sealed record World(Guid ModeratorId, Guid SellerId, Guid ListingId, Guid StoreId);

    /// <summary>
    /// Fails rather than skips. xunit 2.x has no dynamic skip without another package, and between
    /// "silently green with no database" and "loudly red with no database", red is the only honest
    /// option for a project whose whole purpose is to catch what green was hiding.
    /// </summary>
    private void RequireDatabase()
    {
        if (!fixture.Available)
        {
            Assert.Fail(fixture.SkipReason);
        }
    }

    /// <summary>A seller, a moderator, one pending listing and one pending store application.</summary>
    private async Task<World> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        var now = fixture.Clock.UtcNow;

        var seller = new User
        {
            Id = Guid.CreateVersion7(),
            PhoneNumber = $"+9945{Random.Shared.Next(10000000, 99999999)}",
            IsPhoneVerified = true,
            FullName = "Test Satıcı",
            Role = UserRole.User,
            Status = UserStatus.Active,
            CreatedAt = now
        };

        var moderator = new User
        {
            Id = Guid.CreateVersion7(),
            PhoneNumber = $"+9946{Random.Shared.Next(10000000, 99999999)}",
            IsPhoneVerified = true,
            FullName = "Test Moderator",
            Role = UserRole.Admin,
            Status = UserStatus.Active,
            CreatedAt = now
        };

        db.Users.AddRange(seller, moderator);

        var parent = new Category
        {
            Slug = $"kok-{Guid.NewGuid():N}"[..20],
            NameAz = "Kök",
            Depth = 0,
            SortOrder = 10,
            IsActive = true,
            RestrictionStatus = RestrictionStatus.Unrestricted,
            CreatedAt = now
        };

        var region = new Region
        {
            Slug = $"yer-{Guid.NewGuid():N}"[..20],
            NameAz = "Yer",
            Type = RegionType.City,
            IsActive = true,
            IsSelectable = true,
            SortOrder = 10,
            CreatedAt = now
        };

        db.Categories.Add(parent);
        db.Regions.Add(region);
        await db.SaveChangesAsync();

        var leaf = new Category
        {
            Slug = $"yarpaq-{Guid.NewGuid():N}"[..20],
            NameAz = "Yarpaq",
            ParentId = parent.Id,
            Depth = 1,
            SortOrder = 10,
            IsActive = true,
            RestrictionStatus = RestrictionStatus.Unrestricted,
            CreatedAt = now
        };

        db.Categories.Add(leaf);
        await db.SaveChangesAsync();

        var listing = new Listing
        {
            Id = Guid.CreateVersion7(),
            Slug = "yoxlama-elani",
            UserId = seller.Id,
            CategoryId = leaf.Id,
            RegionId = region.Id,
            Title = "Yoxlama elanı",
            Description = "Yoxlama təsviri.",
            Price = 100m,
            Currency = "AZN",
            Condition = ListingCondition.Used,
            SellerType = SellerType.Individual,
            ContactPhone = "+994501112233",
            ShowPhone = true,
            Status = ListingStatus.PendingModeration,
            Attributes = [],
            CreatedAt = now
        };

        var store = new Store
        {
            Id = Guid.CreateVersion7(),
            OwnerUserId = seller.Id,
            Name = "Ovçu Dünyası",
            Slug = $"magaza-{Guid.NewGuid():N}"[..20],
            Status = StoreStatus.PendingVerification,
            CreatedAt = now
        };

        db.Listings.Add(listing);
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        return new World(moderator.Id, seller.Id, listing.Id, store.Id);
    }

    private (IListingModerationService Moderation, IStoreAdminService Stores, AppDbContext Db) Services(Guid actorId)
    {
        var db = fixture.CreateContext();
        var currentUser = new StubCurrentUser { UserId = actorId, Role = "Admin" };
        var categories = new CategoryService(db, new PassthroughTaxonomyCache());

        var notifications = new NotificationService(db, [new InAppNotificationChannel(db, fixture.Clock)], currentUser, fixture.Clock);

        return (
            new ListingModerationService(db, categories, currentUser, new NoOpFileStorage(), fixture.Clock, notifications),
            new StoreAdminService(db, currentUser, fixture.Clock),
            db);
    }

    private async Task<AuditLog> SingleAuditAsync(string action, Guid entityId)
    {
        await using var db = fixture.CreateContext();

        var id = entityId.ToString();

        return await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == action && a.EntityId == id);
    }

    // ---- listing moderation ---------------------------------------------------------------------

    [Fact]
    public async Task Rejecting_a_listing_persists_and_writes_a_structured_reason()
    {
        RequireDatabase();

        var world = await SeedAsync();
        var (moderation, _, db) = Services(world.ModeratorId);
        await using var _db = db;

        var rejected = await moderation.RejectAsync(world.ListingId, "Şəkillər qaydalara uyğun deyil.");

        // Before the fix this threw: a plain-text reason is not valid jsonb.
        Assert.True(rejected.Succeeded);

        var audit = await SingleAuditAsync("listing.moderation.rejected", world.ListingId);
        using var payload = JsonDocument.Parse(audit.PayloadJson!);

        Assert.Equal("Şəkillər qaydalara uyğun deyil.", payload.RootElement.GetProperty("reason").GetString());
        Assert.Equal(world.ModeratorId, audit.ActorUserId);

        await using var check = fixture.CreateContext();
        Assert.Equal(ListingStatus.Rejected, (await check.Listings.FindAsync(world.ListingId))!.Status);
    }

    [Fact]
    public async Task Blocking_a_listing_persists_and_writes_a_structured_reason()
    {
        RequireDatabase();

        var world = await SeedAsync();
        var (moderation, _, db) = Services(world.ModeratorId);
        await using var _db = db;

        await moderation.ApproveAsync(world.ListingId);
        var blocked = await moderation.BlockAsync(world.ListingId, "Qadağan olunmuş məhsul.");

        Assert.True(blocked.Succeeded);

        var audit = await SingleAuditAsync("listing.moderation.blocked", world.ListingId);
        using var payload = JsonDocument.Parse(audit.PayloadJson!);

        Assert.Equal("Qadağan olunmuş məhsul.", payload.RootElement.GetProperty("reason").GetString());

        await using var check = fixture.CreateContext();
        Assert.Equal(ListingStatus.Blocked, (await check.Listings.FindAsync(world.ListingId))!.Status);
    }

    [Fact]
    public async Task Approving_and_unblocking_write_no_payload_at_all()
    {
        RequireDatabase();

        var world = await SeedAsync();
        var (moderation, _, db) = Services(world.ModeratorId);
        await using var _db = db;

        await moderation.ApproveAsync(world.ListingId);
        await moderation.BlockAsync(world.ListingId, "Yoxlama.");
        await moderation.UnblockAsync(world.ListingId);

        // "No reason given" stays null rather than becoming {"reason":null}.
        Assert.Null((await SingleAuditAsync("listing.moderation.approved", world.ListingId)).PayloadJson);
        Assert.Null((await SingleAuditAsync("listing.moderation.unblocked", world.ListingId)).PayloadJson);
    }

    // ---- store moderation: all six verbs ---------------------------------------------------------

    [Fact]
    public async Task Every_store_moderation_verb_persists_against_postgresql()
    {
        RequireDatabase();

        var world = await SeedAsync();
        var (_, stores, db) = Services(world.ModeratorId);
        await using var _db = db;

        // Each of these wrote "Name (slug): reason" into a jsonb column and failed outright.
        Assert.True((await stores.ApproveAsync(world.StoreId)).Succeeded);
        Assert.True((await stores.SetVerifiedAsync(world.StoreId, verified: true)).Succeeded);
        Assert.True((await stores.SetVerifiedAsync(world.StoreId, verified: false)).Succeeded);
        Assert.True((await stores.SuspendAsync(world.StoreId, "Sənədlər yoxlanılır.")).Succeeded);
        Assert.True((await stores.ReinstateAsync(world.StoreId)).Succeeded);

        await using var check = fixture.CreateContext();
        var storeId = world.StoreId.ToString();
        var actions = await check.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "Store" && a.EntityId == storeId)
            .Select(a => a.Action)
            .ToListAsync();

        Assert.Contains("store.approved", actions);
        Assert.Contains("store.verified", actions);
        Assert.Contains("store.unverified", actions);
        Assert.Contains("store.suspended", actions);
        Assert.Contains("store.reinstated", actions);
    }

    [Fact]
    public async Task Rejecting_a_store_application_persists_the_decision_after_the_row_is_gone()
    {
        RequireDatabase();

        var world = await SeedAsync();
        var (_, stores, db) = Services(world.ModeratorId);
        await using var _db = db;

        var rejected = await stores.RejectAsync(world.StoreId, "Ad qaydalara uyğun deyil.");

        Assert.True(rejected.Succeeded);

        var audit = await SingleAuditAsync("store.rejected", world.StoreId);
        using var payload = JsonDocument.Parse(audit.PayloadJson!);

        // The row is deleted, so the trail has to carry what the decision was about.
        Assert.Equal("Ovçu Dünyası", payload.RootElement.GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(payload.RootElement.GetProperty("slug").GetString()));
        Assert.Equal("Ad qaydalara uyğun deyil.", payload.RootElement.GetProperty("reason").GetString());

        await using var check = fixture.CreateContext();
        Assert.Null(await check.Stores.FindAsync(world.StoreId));
    }

    // ---- the column itself -----------------------------------------------------------------------

    [Fact]
    public async Task The_payload_column_is_real_jsonb_and_refuses_plain_text()
    {
        RequireDatabase();

        await using var db = fixture.CreateContext();

        // Guards the reason this project exists: if PayloadJson ever stopped being jsonb, the
        // helper would look like belt-and-braces rather than a requirement.
        var type = await db.Database
            .SqlQuery<string>($"""
                SELECT data_type AS "Value" FROM information_schema.columns
                WHERE table_name = 'AuditLogs' AND column_name = 'PayloadJson'
                """)
            .SingleAsync();

        Assert.Equal("jsonb", type);

        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        Assert.Equal(1, await ExecuteAsync(connection, "'{\"reason\":\"ok\"}'"));

        // The defect, pinned: the column refuses anything that is not JSON.
        var failure = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(
            () => ExecuteAsync(connection, "'plain text'"));

        Assert.Equal("22P02", failure.SqlState);

        static async Task<int> ExecuteAsync(Npgsql.NpgsqlConnection connection, string payloadLiteral)
        {
            await using var command = new Npgsql.NpgsqlCommand(
                """
                INSERT INTO "AuditLogs" ("Id","EntityType","EntityId","Action","PayloadJson","CreatedAt")
                VALUES (gen_random_uuid(), 'Probe', 'x', 'probe', 
                """ + payloadLiteral + ", now())", connection);

            return await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public void Every_helper_shape_produces_valid_json()
    {
        // Cheap, provider-independent, and it pins the shapes the queue and viewer read back.
        Assert.Null(AuditPayload.Reason(null));
        Assert.Null(AuditPayload.Reason("   "));
        Assert.Null(AuditPayload.From(null));
        Assert.Null(AuditPayload.Flags([]));

        foreach (var json in new[]
        {
            AuditPayload.Reason("səbəb"),
            AuditPayload.Named("Ad", "slug"),
            AuditPayload.Named("Ad", "slug", "səbəb"),
            AuditPayload.Flags([("code", "mesaj")]),
            AuditPayload.From(new { anything = 1 })
        })
        {
            using var document = JsonDocument.Parse(json!);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }

        // The queue has always shown "code: message"; the storage shape changed, the contract did not.
        Assert.Equal(["code: mesaj"], AuditPayload.ReadFlagMessages(AuditPayload.Flags([("code", "mesaj")])));

        // Azerbaijani letters survive as themselves, so the trail stays readable.
        Assert.Contains("Ovçu Dünyası", AuditPayload.Named("Ovçu Dünyası", "ovcu-dunyasi")!, StringComparison.Ordinal);
        Assert.Empty(AuditPayload.ReadFlagMessages("not json at all"));
        Assert.Empty(AuditPayload.ReadFlagMessages(null));
    }
}

/// <summary>
/// M-1 against the real database: a partial import must not erase stored coordinates.
/// </summary>
/// <remarks>
/// Worth running here as well as in memory because the columns are real — <c>double precision</c>
/// nullables that a null assignment genuinely overwrites. This is the shape the authoritative
/// Azerbaijani dataset will be loaded in, one correction at a time.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class RegionImportPersistenceTests(PostgresFixture fixture)
{
    private void RequireDatabase()
    {
        if (!fixture.Available)
        {
            Assert.Fail(fixture.SkipReason);
        }
    }

    private async Task<Ovcuprim.Application.Regions.IRegionService> ServiceAsync(AppDbContext db)
    {
        // The collection shares one database, so clear the way the search tests do: a plain DELETE
        // on Regions trips the listing foreign key when another class has left rows behind.
        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE "StoreFollows","Favorites","ListingMedia","ModerationActions","Reports",
                     "Listings","Stores","Regions"
            RESTART IDENTITY CASCADE
            """);

        return new Ovcuprim.Application.Regions.RegionService(db, new PassthroughTaxonomyCache(), fixture.Clock);
    }

    [Fact]
    public async Task A_partial_re_import_keeps_the_coordinates_already_stored()
    {
        RequireDatabase();

        await using var db = fixture.CreateContext();
        var service = await ServiceAsync(db);

        await service.ImportAsync([new(
            "Masallı", "Масаллы", null, "Rayon", null, IsSelectable: true, 39.0345, 48.6614, 10)]);

        // A later dataset that carries no coordinates at all.
        await service.ImportAsync([new(
            "Masallı",
            Ovcuprim.Application.Common.Omittable<string>.Absent,
            null, "Rayon", null, IsSelectable: true,
            Ovcuprim.Application.Common.Omittable<double?>.Absent,
            Ovcuprim.Application.Common.Omittable<double?>.Absent,
            null)]);

        await using var check = fixture.CreateContext();
        var region = await check.Regions.AsNoTracking().SingleAsync(r => r.Slug == "masalli");

        Assert.Equal(39.0345, region.Latitude);
        Assert.Equal(48.6614, region.Longitude);
        Assert.Equal("Масаллы", region.NameRu);
        Assert.Equal(10, region.SortOrder);
    }

    [Fact]
    public async Task An_explicit_null_clears_the_column()
    {
        RequireDatabase();

        await using var db = fixture.CreateContext();
        var service = await ServiceAsync(db);

        await service.ImportAsync([new(
            "Lerik", "Лерик", null, "Rayon", null, IsSelectable: true, 38.7742, 48.4153, 20)]);

        await service.ImportAsync([new(
            "Lerik",
            new Ovcuprim.Application.Common.Omittable<string>(null),
            null, "Rayon", null, IsSelectable: true,
            new Ovcuprim.Application.Common.Omittable<double?>(null),
            new Ovcuprim.Application.Common.Omittable<double?>(null),
            null)]);

        await using var check = fixture.CreateContext();
        var region = await check.Regions.AsNoTracking().SingleAsync(r => r.Slug == "lerik");

        Assert.Null(region.Latitude);
        Assert.Null(region.Longitude);
        Assert.Null(region.NameRu);
    }
}
