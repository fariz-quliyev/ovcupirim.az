using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Infrastructure.Persistence.Seed;

public sealed record SyntheticDataReport(int Users, int Listings, int Media)
{
    public static SyntheticDataReport Empty { get; } = new(0, 0, 0);
}

/// <summary>
/// Fills the database with plausible listings so query plans and page timings can be measured
/// against realistic volume.
/// </summary>
/// <remarks>
/// <para>
/// <b>Development only.</b> The caller must refuse to run this outside a development environment;
/// every row it writes is synthetic and must never reach a real deployment. Sellers are created
/// with a reserved phone prefix and titles carry a marker so the data is identifiable and
/// removable.
/// </para>
/// <para>
/// Attribute values are generated from the real schema — the same definitions and options the
/// taxonomy seeder installed — so the generated JSONB exercises the production expression and GIN
/// indexes rather than a shape invented here.
/// </para>
/// </remarks>
public interface ISyntheticListingGenerator
{
    Task<SyntheticDataReport> GenerateAsync(int listingCount, CancellationToken cancellationToken = default);

    /// <summary>Removes everything <see cref="GenerateAsync"/> created, and nothing else.</summary>
    Task<SyntheticDataReport> PurgeAsync(CancellationToken cancellationToken = default);
}

public sealed class SyntheticListingGenerator(
    AppDbContext db,
    IDateTimeProvider clock,
    ILogger<SyntheticListingGenerator> logger) : ISyntheticListingGenerator
{
    /// <summary>Marks a synthetic seller. Not a dialable Azerbaijani mobile prefix.</summary>
    private const string SellerPhonePrefix = "+99450000";

    /// <summary>Appended to every generated title so the rows are identifiable at a glance.</summary>
    public const string Marker = "[sintetik]";

    private const int SellerCount = 200;
    private const int BatchSize = 2_000;

    private static readonly string[] TitleWords =
    [
        "Professional", "Yüngül", "Su keçirməyən", "Kompakt", "Klassik", "Universal",
        "İkiqat", "Qatlanan", "Termo", "Kamuflyaj", "Möhkəm", "Yeni nəsil"
    ];

    private static readonly string[] Brands =
    [
        "Naturehike", "Deuter", "Shimano", "Daiwa", "Bushnell", "Leatherman",
        "Fiskars", "Coleman", "Petzl", "Garmin", "Vortex", "Helikon"
    ];

    public async Task<SyntheticDataReport> GenerateAsync(int listingCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(listingCount, 1);

        // Deterministic: the same request produces the same corpus, so a timing comparison between
        // two runs is a comparison of the code and not of the data.
        var random = new Random(20260902);

        var leaves = await LeafCategoriesAsync(cancellationToken);

        if (leaves.Count == 0)
        {
            logger.LogWarning("No leaf categories found. Run the taxonomy seeder first.");
            return SyntheticDataReport.Empty;
        }

        var regionIds = await db.Regions.AsNoTracking()
            .Where(r => r.IsActive && r.IsSelectable)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (regionIds.Count == 0)
        {
            logger.LogWarning("No selectable regions found. Run the taxonomy seeder first.");
            return SyntheticDataReport.Empty;
        }

        var schemas = await SchemasAsync(leaves.Select(l => l.Id).ToList(), cancellationToken);
        var sellerIds = await EnsureSellersAsync(random, cancellationToken);

        var now = clock.UtcNow;
        var listings = 0;
        var media = 0;
        var batch = new List<Listing>(BatchSize);
        var batchMedia = new List<ListingMedia>(BatchSize);

        for (var i = 0; i < listingCount; i++)
        {
            var leaf = leaves[random.Next(leaves.Count)];
            var listing = BuildListing(random, leaf, regionIds, sellerIds, schemas, now);

            batch.Add(listing);

            // Most listings carry a picture, which is what the card query has to hydrate.
            if (random.Next(100) < 80)
            {
                batchMedia.Add(BuildMedia(listing, now));
            }

            if (batch.Count >= BatchSize)
            {
                listings += await FlushAsync(batch, batchMedia, cancellationToken);
                media += batchMedia.Count;
                batch.Clear();
                batchMedia.Clear();
                logger.LogInformation("Generated {Count} of {Total} listings.", listings, listingCount);
            }
        }

        if (batch.Count > 0)
        {
            listings += await FlushAsync(batch, batchMedia, cancellationToken);
            media += batchMedia.Count;
        }

        return new SyntheticDataReport(sellerIds.Count, listings, media);
    }

    public async Task<SyntheticDataReport> PurgeAsync(CancellationToken cancellationToken = default)
    {
        // Ordered by dependency; the marker and the reserved prefix are the only things removed.
        var listings = await db.Listings.IgnoreQueryFilters()
            .Where(l => l.Title.EndsWith(Marker))
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

        var mediaRemoved = await db.ListingMedia.IgnoreQueryFilters()
            .Where(m => listings.Contains(m.ListingId))
            .ExecuteDeleteAsync(cancellationToken);

        var listingsRemoved = await db.Listings.IgnoreQueryFilters()
            .Where(l => listings.Contains(l.Id))
            .ExecuteDeleteAsync(cancellationToken);

        var usersRemoved = await db.Users.IgnoreQueryFilters()
            .Where(u => u.PhoneNumber.StartsWith(SellerPhonePrefix))
            .ExecuteDeleteAsync(cancellationToken);

        return new SyntheticDataReport(usersRemoved, listingsRemoved, mediaRemoved);
    }

    private async Task<int> FlushAsync(
        List<Listing> listings, List<ListingMedia> media, CancellationToken cancellationToken)
    {
        db.Listings.AddRange(listings);
        db.ListingMedia.AddRange(media);
        await db.SaveChangesAsync(cancellationToken);

        // Keep the change tracker from growing across batches.
        db.ChangeTracker.Clear();

        return listings.Count;
    }

    private Listing BuildListing(
        Random random,
        LeafCategory leaf,
        List<int> regionIds,
        List<Guid> sellerIds,
        Dictionary<int, List<AttributeDefinition>> schemas,
        DateTimeOffset now)
    {
        var title = $"{TitleWords[random.Next(TitleWords.Length)]} {leaf.NameAz} {Marker}";
        var brand = Brands[random.Next(Brands.Length)];
        var status = PickStatus(random);
        var publishedAt = now.AddDays(-random.Next(0, 30)).AddMinutes(-random.Next(0, 1440));

        var listing = new Listing
        {
            Id = Guid.CreateVersion7(),
            UserId = sellerIds[random.Next(sellerIds.Count)],
            CategoryId = leaf.Id,
            RegionId = regionIds[random.Next(regionIds.Count)],
            Title = Truncate(title, 70),
            Slug = ListingSlug(title),
            Description = $"{title}. Sınaq məqsədilə yaradılmış nümunə elan. Vəziyyəti yaxşıdır, sənədləri tamdır.",
            // A tenth carry no price at all, which is the "Razılaşma ilə" case the UI has to render.
            Price = random.Next(10) == 0 ? null : Math.Round((decimal)(random.NextDouble() * 3000) + 5, 2),
            Currency = "AZN",
            Condition = random.Next(3) == 0 ? ListingCondition.New : ListingCondition.Used,
            HasDelivery = random.Next(2) == 0,
            Brand = brand,
            SellerType = SellerType.Individual,
            ContactPhone = $"{SellerPhonePrefix}{random.Next(0, 100):00}",
            ShowPhone = random.Next(10) != 0,
            Status = status,
            Attributes = BuildAttributes(random, schemas.GetValueOrDefault(leaf.Id, [])),
            SearchKey = ListingSearchKey(title, brand),
            CreatedAt = publishedAt
        };

        if (status is ListingStatus.Active or ListingStatus.Sold or ListingStatus.Expired)
        {
            listing.PublishedAt = publishedAt;
            listing.BumpedAt = publishedAt;
            listing.ExpiresAt = publishedAt.AddDays(30);
        }

        return listing;
    }

    /// <summary>
    /// Weighted to what the catalogue actually queries: most rows are live, with a realistic tail
    /// of other states so the Status filter is doing real work rather than matching everything.
    /// </summary>
    private static ListingStatus PickStatus(Random random) => random.Next(100) switch
    {
        < 80 => ListingStatus.Active,
        < 86 => ListingStatus.Expired,
        < 91 => ListingStatus.Sold,
        < 95 => ListingStatus.PendingModeration,
        < 98 => ListingStatus.Rejected,
        _ => ListingStatus.Draft
    };

    private static Dictionary<string, JsonElement> BuildAttributes(
        Random random, List<AttributeDefinition> definitions)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            // Optional fields are filled most of the time, so a filter on one still returns rows
            // while the "missing key" path stays represented.
            if (!definition.IsRequired && random.Next(100) < 25)
            {
                continue;
            }

            var json = definition.DataType switch
            {
                AttributeDataType.Number => NumberJson(random, definition),
                AttributeDataType.Boolean => random.Next(2) == 0 ? "true" : "false",
                AttributeDataType.Select => OptionJson(random, definition, multiple: false),
                AttributeDataType.MultiSelect => OptionJson(random, definition, multiple: true),
                _ => JsonSerializer.Serialize($"{definition.LabelAz} nümunə")
            };

            if (json is not null)
            {
                values[definition.Key] = JsonDocument.Parse(json).RootElement.Clone();
            }
        }

        return values;
    }

    private static string NumberJson(Random random, AttributeDefinition definition)
    {
        var min = (double)(definition.MinValue ?? 0m);
        var max = (double)(definition.MaxValue ?? 100m);

        if (max <= min)
        {
            max = min + 100;
        }

        var value = min + (random.NextDouble() * (max - min));
        var places = definition.DecimalPlaces ?? 0;

        return Math.Round(value, places).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? OptionJson(Random random, AttributeDefinition definition, bool multiple)
    {
        var options = definition.Options.Where(o => o.IsActive).Select(o => o.Value).ToList();

        if (options.Count == 0)
        {
            return null;
        }

        if (!multiple)
        {
            return JsonSerializer.Serialize(options[random.Next(options.Count)]);
        }

        var picked = options.OrderBy(_ => random.Next()).Take(random.Next(1, Math.Min(3, options.Count) + 1)).ToList();

        return JsonSerializer.Serialize(picked);
    }

    private static ListingMedia BuildMedia(Listing listing, DateTimeOffset now)
    {
        // Keys are unique and deliberately point at nothing: the generator writes no files, so the
        // card query is exercised without inventing image bytes.
        var key = $"synthetic/{listing.Id:N}/cover";

        return new ListingMedia
        {
            Id = Guid.CreateVersion7(),
            ListingId = listing.Id,
            StorageKey = $"{key}.webp",
            ContentType = "image/webp",
            Width = 1600,
            Height = 1200,
            SizeBytes = 120_000,
            SortOrder = 0,
            IsPrimary = true,
            Variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["thumb"] = $"{key}_thumb.webp",
                ["card"] = $"{key}_card.webp",
                ["detail"] = $"{key}_detail.webp",
                ["og"] = $"{key}_og.webp"
            },
            CreatedAt = now
        };
    }

    private async Task<List<Guid>> EnsureSellersAsync(Random random, CancellationToken cancellationToken)
    {
        var existing = await db.Users.AsNoTracking()
            .Where(u => u.PhoneNumber.StartsWith(SellerPhonePrefix))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        if (existing.Count >= SellerCount)
        {
            return existing;
        }

        var now = clock.UtcNow;
        var created = new List<User>();

        for (var i = existing.Count; i < SellerCount; i++)
        {
            created.Add(new User
            {
                Id = Guid.CreateVersion7(),
                PhoneNumber = $"{SellerPhonePrefix}{i:00}",
                IsPhoneVerified = true,
                FullName = $"Sintetik Satıcı {i + 1:000}",
                Role = UserRole.User,
                Status = UserStatus.Active,
                CreatedAt = now.AddDays(-random.Next(30, 400))
            });
        }

        db.Users.AddRange(created);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        return [.. existing, .. created.Select(u => u.Id)];
    }

    private async Task<List<LeafCategory>> LeafCategoriesAsync(CancellationToken cancellationToken)
    {
        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new { c.Id, c.ParentId, c.NameAz })
            .ToListAsync(cancellationToken);

        var parentIds = categories.Where(c => c.ParentId is not null).Select(c => c.ParentId!.Value).ToHashSet();

        return categories
            .Where(c => !parentIds.Contains(c.Id))
            .Select(c => new LeafCategory(c.Id, c.NameAz))
            .ToList();
    }

    private async Task<Dictionary<int, List<AttributeDefinition>>> SchemasAsync(
        List<int> leafIds, CancellationToken cancellationToken)
    {
        // Own definitions plus anything an ancestor shares, resolved the same way the category
        // service resolves it, so generated values match the schema a filter will be built from.
        var categories = await db.Categories.AsNoTracking()
            .Select(c => new { c.Id, c.ParentId })
            .ToListAsync(cancellationToken);

        var parentById = categories.ToDictionary(c => c.Id, c => c.ParentId);

        var definitions = await db.AttributeDefinitions.AsNoTracking()
            .Where(a => a.IsActive)
            .Include(a => a.Options)
            .ToListAsync(cancellationToken);

        var byCategory = definitions.GroupBy(d => d.CategoryId).ToDictionary(g => g.Key, g => g.ToList());
        var result = new Dictionary<int, List<AttributeDefinition>>();

        foreach (var leafId in leafIds)
        {
            var resolved = new Dictionary<string, AttributeDefinition>(StringComparer.Ordinal);
            int? cursor = leafId;
            var own = true;

            while (cursor is { } id)
            {
                foreach (var definition in byCategory.GetValueOrDefault(id, []))
                {
                    if ((own || definition.AppliesToDescendants) && !resolved.ContainsKey(definition.Key))
                    {
                        resolved[definition.Key] = definition;
                    }
                }

                cursor = parentById.GetValueOrDefault(id);
                own = false;
            }

            result[leafId] = [.. resolved.Values];
        }

        return result;
    }

    private static string ListingSlug(string title) => AzerbaijaniText.ToSlug(title, 120);

    private static string? ListingSearchKey(string title, string brand)
    {
        var normalized = AzerbaijaniText.Normalize($"{title} {brand}");
        return normalized.Length == 0 ? null : normalized[..Math.Min(normalized.Length, 200)];
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd();

    private sealed record LeafCategory(int Id, string NameAz);
}
