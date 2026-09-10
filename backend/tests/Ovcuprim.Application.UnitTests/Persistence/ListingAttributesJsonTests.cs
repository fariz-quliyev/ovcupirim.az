using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.UnitTests.Auth;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;
using Ovcuprim.Infrastructure.Persistence;

namespace Ovcuprim.Application.UnitTests.Persistence;

/// <summary>
/// The round-trip spike for the dynamic attribute bag. It proves the canonical JSON types survive
/// persistence, that the value comparer detects a mutation, and that an untouched entity is not
/// reported as dirty.
/// </summary>
public class ListingAttributesJsonTests : IDisposable
{
    private readonly string _database = $"attrs-{Guid.NewGuid()}";
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly List<AppDbContext> _contexts = [];

    private AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_database)
            .Options;

        var context = new AppDbContext(options, _clock);
        _contexts.Add(context);
        return context;
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private async Task<Guid> SeedListingAsync(Dictionary<string, JsonElement> attributes)
    {
        await using var db = NewContext();

        var user = new User { Id = Guid.NewGuid(), PhoneNumber = "+994501234567", FullName = "Test" };
        var category = new Category { Id = 1, NameAz = "Tilovlar", Slug = "tilovlar", Depth = 1 };
        var region = new Region { Id = 1, NameAz = "Bakı", Slug = "baki" };

        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            Slug = "test-elan",
            UserId = user.Id,
            CategoryId = category.Id,
            RegionId = region.Id,
            Title = "Test elan",
            Description = "Təsvir",
            Price = 100m,
            ContactPhone = "+994501234567",
            Condition = ListingCondition.New,
            SellerType = SellerType.Individual,
            Status = ListingStatus.Active,
            Attributes = attributes
        };

        db.Users.Add(user);
        db.Categories.Add(category);
        db.Regions.Add(region);
        db.Listings.Add(listing);
        await db.SaveChangesAsync();

        return listing.Id;
    }

    [Fact]
    public async Task Round_trips_every_canonical_json_type()
    {
        var id = await SeedListingAsync(new Dictionary<string, JsonElement>
        {
            ["material"] = Json("\"karbon\""),          // Text / Select -> string
            ["length"] = Json("2.40"),                   // Number        -> number
            ["waterproof"] = Json("true"),               // Boolean       -> boolean
            ["features"] = Json("[\"kelbetin\",\"bicaq\"]") // MultiSelect -> array of strings
        });

        await using var db = NewContext();
        var listing = await db.Listings.SingleAsync(l => l.Id == id);

        Assert.Equal(JsonValueKind.String, listing.Attributes["material"].ValueKind);
        Assert.Equal("karbon", listing.Attributes["material"].GetString());

        Assert.Equal(JsonValueKind.Number, listing.Attributes["length"].ValueKind);
        Assert.Equal(2.40m, listing.Attributes["length"].GetDecimal());

        Assert.Equal(JsonValueKind.True, listing.Attributes["waterproof"].ValueKind);
        Assert.True(listing.Attributes["waterproof"].GetBoolean());

        Assert.Equal(JsonValueKind.Array, listing.Attributes["features"].ValueKind);

        var features = listing.Attributes["features"].EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        Assert.Equal(new[] { "kelbetin", "bicaq" }, features);
    }

    [Fact]
    public async Task MultiSelect_survives_as_an_array_not_a_joined_string()
    {
        var id = await SeedListingAsync(new Dictionary<string, JsonElement>
        {
            ["features"] = Json("[\"kelbetin\",\"bicaq\",\"misar\"]")
        });

        await using var db = NewContext();
        var listing = await db.Listings.SingleAsync(l => l.Id == id);

        var raw = listing.Attributes["features"].GetRawText();

        Assert.StartsWith("[", raw, StringComparison.Ordinal);
        Assert.EndsWith("]", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("kelbetin,bicaq", raw, StringComparison.Ordinal);
        Assert.Equal(3, listing.Attributes["features"].GetArrayLength());
    }

    [Fact]
    public async Task An_element_stays_readable_after_the_context_is_disposed()
    {
        // Guards the JsonElement lifetime concern: each value is parsed into its own document,
        // so it does not become unusable when the loading context goes away.
        var id = await SeedListingAsync(new Dictionary<string, JsonElement>
        {
            ["features"] = Json("[\"kelbetin\"]")
        });

        JsonElement captured;

        await using (var db = NewContext())
        {
            var listing = await db.Listings.SingleAsync(l => l.Id == id);
            captured = listing.Attributes["features"];
        }

        Assert.Equal(1, captured.GetArrayLength());
        Assert.Equal("kelbetin", captured[0].GetString());
    }

    [Fact]
    public async Task The_comparer_detects_a_mutated_attribute_bag()
    {
        var id = await SeedListingAsync(new Dictionary<string, JsonElement>
        {
            ["material"] = Json("\"karbon\"")
        });

        await using (var db = NewContext())
        {
            var listing = await db.Listings.SingleAsync(l => l.Id == id);
            listing.Attributes["material"] = Json("\"kompozit\"");

            Assert.True(db.ChangeTracker.HasChanges(), "A changed attribute value must mark the entity dirty.");
            await db.SaveChangesAsync();
        }

        await using var verify = NewContext();
        var reloaded = await verify.Listings.SingleAsync(l => l.Id == id);

        Assert.Equal("kompozit", reloaded.Attributes["material"].GetString());
    }

    [Fact]
    public async Task Adding_a_key_marks_the_entity_dirty()
    {
        var id = await SeedListingAsync(new Dictionary<string, JsonElement>
        {
            ["material"] = Json("\"karbon\"")
        });

        await using (var db = NewContext())
        {
            var listing = await db.Listings.SingleAsync(l => l.Id == id);
            listing.Attributes["length"] = Json("2.40");

            Assert.True(db.ChangeTracker.HasChanges());
            await db.SaveChangesAsync();
        }

        await using var verify = NewContext();
        var reloaded = await verify.Listings.SingleAsync(l => l.Id == id);

        Assert.Equal(2, reloaded.Attributes.Count);
        Assert.Equal(2.40m, reloaded.Attributes["length"].GetDecimal());
    }

    [Fact]
    public async Task An_untouched_listing_is_not_reported_as_dirty()
    {
        var id = await SeedListingAsync(new Dictionary<string, JsonElement>
        {
            ["material"] = Json("\"karbon\""),
            ["features"] = Json("[\"kelbetin\",\"bicaq\"]")
        });

        await using var db = NewContext();
        var listing = await db.Listings.SingleAsync(l => l.Id == id);

        // Reading the values must not make the snapshot look different.
        _ = listing.Attributes["material"].GetString();
        _ = listing.Attributes["features"].GetArrayLength();

        Assert.False(db.ChangeTracker.HasChanges(), "Reading attributes must not dirty the entity.");
    }

    [Fact]
    public async Task An_empty_attribute_bag_round_trips()
    {
        var id = await SeedListingAsync([]);

        await using var db = NewContext();
        var listing = await db.Listings.SingleAsync(l => l.Id == id);

        Assert.Empty(listing.Attributes);
    }

    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
