using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;
using Ovcuprim.Infrastructure.Persistence;

namespace Ovcuprim.PostgresTests;

/// <summary>
/// A small but realistic corpus for the catalogue SQL: a taxonomy with attributes, two regions,
/// a storefront, and listings spread across the statuses the public predicate has to exclude.
/// </summary>
/// <remarks>
/// Built once per test class against its own database, so the counts these tests assert are exact
/// rather than "at least". Everything here exists because some hand-written statement reads it —
/// the JSONB attribute bag, the generated tsvector, the folded SearchKey, favourites, followers.
/// </remarks>
public sealed class SearchWorld
{
    public required Guid SellerId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid StoreId { get; init; }

    public required int RootCategoryId { get; init; }

    public required int TentCategoryId { get; init; }

    public required int RodCategoryId { get; init; }

    public required int BakuRegionId { get; init; }

    public required int GanjaRegionId { get; init; }

    /// <summary>Live listings only — what every public query should find.</summary>
    public required IReadOnlyList<Guid> ActiveListingIds { get; init; }

    public required Guid StoreListingId { get; init; }

    public required Guid FavouritedListingId { get; init; }

    public static async Task<SearchWorld> SeedAsync(AppDbContext db, DateTimeOffset now)
    {
        var seller = new User
        {
            Id = Guid.CreateVersion7(),
            PhoneNumber = $"+9945{Random.Shared.Next(10000000, 99999999)}",
            IsPhoneVerified = true,
            FullName = "Satıcı",
            Role = UserRole.User,
            Status = UserStatus.Active,
            CreatedAt = now
        };

        var buyer = new User
        {
            Id = Guid.CreateVersion7(),
            PhoneNumber = $"+9946{Random.Shared.Next(10000000, 99999999)}",
            IsPhoneVerified = true,
            FullName = "Alıcı",
            Role = UserRole.User,
            Status = UserStatus.Active,
            CreatedAt = now
        };

        db.Users.AddRange(seller, buyer);

        var baku = new Region
        {
            Slug = "baki",
            NameAz = "Bakı",
            Type = RegionType.City,
            IsActive = true,
            IsSelectable = true,
            SortOrder = 10,
            CreatedAt = now
        };

        var ganja = new Region
        {
            Slug = "gence",
            NameAz = "Gəncə",
            Type = RegionType.City,
            IsActive = true,
            IsSelectable = true,
            SortOrder = 20,
            CreatedAt = now
        };

        db.Regions.AddRange(baku, ganja);

        var root = new Category
        {
            Slug = "kamp",
            NameAz = "Kamp",
            Depth = 0,
            SortOrder = 10,
            IsActive = true,
            RestrictionStatus = RestrictionStatus.Unrestricted,
            CreatedAt = now
        };

        db.Categories.Add(root);
        await db.SaveChangesAsync();

        var tents = new Category
        {
            Slug = "cadirlar",
            NameAz = "Çadırlar",
            ParentId = root.Id,
            Depth = 1,
            SortOrder = 10,
            IsActive = true,
            RestrictionStatus = RestrictionStatus.Unrestricted,
            CreatedAt = now
        };

        var rods = new Category
        {
            Slug = "tilovlar",
            NameAz = "Tilovlar",
            ParentId = root.Id,
            Depth = 1,
            SortOrder = 20,
            IsActive = true,
            RestrictionStatus = RestrictionStatus.Unrestricted,
            CreatedAt = now
        };

        db.Categories.AddRange(tents, rods);
        await db.SaveChangesAsync();

        var capacity = new AttributeDefinition
        {
            CategoryId = tents.Id,
            Key = "capacity_person",
            LabelAz = "Tutum",
            DataType = AttributeDataType.Select,
            IsRequired = false,
            IsActive = true,
            IsFilterable = true,
            SortOrder = 10,
            CreatedAt = now
        };

        capacity.Options.Add(new AttributeOption { Value = "2", LabelAz = "2 nəfər", SortOrder = 10, IsActive = true });
        capacity.Options.Add(new AttributeOption { Value = "4", LabelAz = "4 nəfər", SortOrder = 20, IsActive = true });

        db.AttributeDefinitions.AddRange(
            capacity,
            new AttributeDefinition
            {
                CategoryId = tents.Id,
                Key = "weight",
                LabelAz = "Çəki",
                DataType = AttributeDataType.Number,
                Unit = "kq",
                MinValue = 0,
                MaxValue = 100,
                DecimalPlaces = 2,
                IsActive = true,
                IsFilterable = true,
                SortOrder = 20,
                CreatedAt = now
            },
            new AttributeDefinition
            {
                CategoryId = tents.Id,
                Key = "waterproof",
                LabelAz = "Sukeçirməz",
                DataType = AttributeDataType.Boolean,
                IsActive = true,
                IsFilterable = true,
                SortOrder = 30,
                CreatedAt = now
            });

        var store = new Store
        {
            Id = Guid.CreateVersion7(),
            OwnerUserId = seller.Id,
            Name = "Ovçu Dünyası",
            Slug = $"magaza-{Guid.NewGuid():N}"[..20],
            Status = StoreStatus.Active,
            CreatedAt = now
        };

        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var active = new List<Guid>();

        // Live listings, deliberately varied so each filter can be told apart from the others.
        var tent2 = Add(db, seller.Id, null, tents.Id, baku.Id, "Çadır iki nəfərlik", "Yüngül çadır.",
            120m, ListingCondition.Used, hasDelivery: true,
            """{"capacity_person":"2","weight":2.5,"waterproof":true}""", now.AddMinutes(-10));

        var tent4 = Add(db, seller.Id, null, tents.Id, ganja.Id, "Çadır dörd nəfərlik", "Ailə çadırı.",
            300m, ListingCondition.New, hasDelivery: false,
            """{"capacity_person":"4","weight":6.0,"waterproof":false}""", now.AddMinutes(-20));

        var rod = Add(db, seller.Id, null, rods.Id, baku.Id, "Tilov dəsti", "Balıqçılıq üçün.",
            80m, ListingCondition.Used, hasDelivery: false, "{}", now.AddMinutes(-30));

        var storeTent = Add(db, seller.Id, store.Id, tents.Id, baku.Id, "Mağaza çadırı", "Mağazadan.",
            null, ListingCondition.New, hasDelivery: true,
            """{"capacity_person":"2","weight":3.0,"waterproof":true}""", now.AddMinutes(-40));

        active.AddRange([tent2.Id, tent4.Id, rod.Id, storeTent.Id]);

        // Excluded from every public read: the predicate is "Status = 2 AND DeletedAt IS NULL".
        Add(db, seller.Id, null, tents.Id, baku.Id, "Gözləyən çadır", "Moderasiyada.",
            50m, ListingCondition.Used, hasDelivery: false, "{}", now, ListingStatus.PendingModeration);

        var deleted = Add(db, seller.Id, null, tents.Id, baku.Id, "Silinmiş çadır", "Silinib.",
            50m, ListingCondition.Used, hasDelivery: false, "{}", now);
        deleted.DeletedAt = now;

        await db.SaveChangesAsync();

        db.Favorites.Add(new Favorite { UserId = buyer.Id, ListingId = tent2.Id, CreatedAt = now });
        db.StoreFollows.Add(new StoreFollow { UserId = buyer.Id, StoreId = store.Id, CreatedAt = now });

        await db.SaveChangesAsync();

        return new SearchWorld
        {
            SellerId = seller.Id,
            BuyerId = buyer.Id,
            StoreId = store.Id,
            RootCategoryId = root.Id,
            TentCategoryId = tents.Id,
            RodCategoryId = rods.Id,
            BakuRegionId = baku.Id,
            GanjaRegionId = ganja.Id,
            ActiveListingIds = active,
            StoreListingId = storeTent.Id,
            FavouritedListingId = tent2.Id,
        };
    }

    private static Listing Add(
        AppDbContext db,
        Guid userId,
        Guid? storeId,
        int categoryId,
        int regionId,
        string title,
        string description,
        decimal? price,
        ListingCondition condition,
        bool hasDelivery,
        string attributesJson,
        DateTimeOffset bumpedAt,
        ListingStatus status = ListingStatus.Active)
    {
        var listing = new Listing
        {
            Id = Guid.CreateVersion7(),
            Slug = $"elan-{Guid.NewGuid():N}"[..24],
            UserId = userId,
            StoreId = storeId,
            CategoryId = categoryId,
            RegionId = regionId,
            Title = title,
            Description = description,
            Price = price,
            Currency = "AZN",
            Condition = condition,
            HasDelivery = hasDelivery,
            SellerType = storeId is null ? SellerType.Individual : SellerType.Store,
            ContactPhone = "+994501112233",
            ShowPhone = true,
            Status = status,
            Attributes = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(attributesJson)!,
            // Folded exactly the way the application builds it, so the trigram fallback is exercised
            // against real stored data rather than against a convenient spelling.
            SearchKey = Application.Common.AzerbaijaniText.Normalize(title),
            PublishedAt = bumpedAt,
            BumpedAt = bumpedAt,
            ExpiresAt = bumpedAt.AddDays(30),
            CreatedAt = bumpedAt,
        };

        db.Listings.Add(listing);

        return listing;
    }
}
