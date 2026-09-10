using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Infrastructure.Persistence.Seed;

public interface ITaxonomySeeder
{
    /// <summary>
    /// Inserts anything missing and leaves everything present untouched, so an administrator's
    /// edits survive the next deployment. Safe to run repeatedly.
    /// </summary>
    Task<SeedReport> SeedAsync(bool includeDevelopmentRegions, CancellationToken cancellationToken = default);
}

public sealed class TaxonomySeeder(
    AppDbContext db,
    IDateTimeProvider clock,
    ILogger<TaxonomySeeder> logger) : ITaxonomySeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public async Task<SeedReport> SeedAsync(bool includeDevelopmentRegions, CancellationToken cancellationToken = default)
    {
        var categories = await SeedCategoriesAsync(cancellationToken);
        var (definitions, options) = await SeedAttributesAsync(cancellationToken);
        var regions = includeDevelopmentRegions ? await SeedDevelopmentRegionsAsync(cancellationToken) : 0;
        var pages = await SeedPagesAsync(cancellationToken);
        var (faqCategories, faqItems) = await SeedFaqAsync(cancellationToken);

        var report = new SeedReport(categories, definitions, options, regions, pages, faqCategories, faqItems);

        logger.LogInformation(
            "Seed complete: {Categories} categories, {Definitions} attribute definitions, {Options} options, " +
            "{Regions} regions, {Pages} pages, {FaqCategories} FAQ categories, {FaqItems} FAQ items",
            report.Categories, report.AttributeDefinitions, report.AttributeOptions,
            report.Regions, report.StaticPages, report.FaqCategories, report.FaqItems);

        return report;
    }

    private async Task<int> SeedCategoriesAsync(CancellationToken cancellationToken)
    {
        var seeds = Load<List<CategorySeed>>("categories.json");
        var existing = await db.Categories.ToDictionaryAsync(c => c.Slug, cancellationToken);
        var inserted = 0;

        foreach (var top in seeds)
        {
            var parent = existing.GetValueOrDefault(top.Slug);

            if (parent is null)
            {
                parent = ToCategory(top, parentId: null, depth: 0);
                db.Categories.Add(parent);
                await db.SaveChangesAsync(cancellationToken);
                existing[parent.Slug] = parent;
                inserted++;
            }

            foreach (var child in top.Children)
            {
                if (existing.ContainsKey(child.Slug))
                {
                    continue;
                }

                // A child with no explicit status inherits its parent's, so the tree stays
                // consistent without repeating the value on every subcategory.
                var entity = ToCategory(child, parent.Id, depth: 1);
                entity.RestrictionStatus = child.RestrictionStatus == Domain.Enums.RestrictionStatus.Unclassified
                    ? parent.RestrictionStatus
                    : child.RestrictionStatus;

                db.Categories.Add(entity);
                existing[child.Slug] = entity;
                inserted++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return inserted;
    }

    private Category ToCategory(CategorySeed seed, int? parentId, int depth) => new()
    {
        ParentId = parentId,
        Depth = depth,
        Slug = string.IsNullOrWhiteSpace(seed.Slug) ? AzerbaijaniText.ToSlug(seed.NameAz) : seed.Slug,
        NameAz = seed.NameAz,
        NameRu = seed.NameRu,
        IconKey = seed.IconKey,
        DescriptionAz = seed.DescriptionAz,
        DescriptionRu = seed.DescriptionRu,
        SortOrder = seed.SortOrder,
        RestrictionStatus = seed.RestrictionStatus,
        IsActive = true,
        IsSelectable = true,
        CreatedAt = clock.UtcNow
    };

    private async Task<(int Definitions, int Options)> SeedAttributesAsync(CancellationToken cancellationToken)
    {
        var file = Load<AttributeSeedFile>("attributes.json");
        var categories = await db.Categories.ToDictionaryAsync(c => c.Slug, c => c.Id, cancellationToken);

        var existing = await db.AttributeDefinitions
            .Select(a => new { a.CategoryId, a.Key })
            .ToListAsync(cancellationToken);

        var present = existing.Select(x => (x.CategoryId, x.Key)).ToHashSet();

        var definitions = 0;
        var options = 0;

        foreach (var seed in file.Attributes)
        {
            if (!categories.TryGetValue(seed.Category, out var categoryId))
            {
                throw new InvalidOperationException(
                    $"Attribute '{seed.Key}' references unknown category '{seed.Category}'.");
            }

            if (present.Contains((categoryId, seed.Key)))
            {
                continue;
            }

            var definition = new AttributeDefinition
            {
                CategoryId = categoryId,
                Key = seed.Key,
                LabelAz = seed.LabelAz,
                LabelRu = seed.LabelRu,
                DataType = seed.DataType,
                Unit = seed.Unit,
                PlaceholderAz = seed.PlaceholderAz,
                PlaceholderRu = seed.PlaceholderRu,
                HelpTextAz = seed.HelpTextAz,
                HelpTextRu = seed.HelpTextRu,
                IsRequired = seed.IsRequired,
                IsFilterable = seed.IsFilterable,
                IsSearchable = seed.IsSearchable,
                IsActive = true,
                AppliesToDescendants = seed.AppliesToDescendants,
                MinValue = seed.MinValue,
                MaxValue = seed.MaxValue,
                DecimalPlaces = seed.DecimalPlaces,
                MaxLength = seed.MaxLength,
                SortOrder = seed.SortOrder,
                CreatedAt = clock.UtcNow
            };

            if (seed.OptionSet is not null)
            {
                if (!file.OptionSets.TryGetValue(seed.OptionSet, out var set))
                {
                    throw new InvalidOperationException(
                        $"Attribute '{seed.Key}' references unknown option set '{seed.OptionSet}'.");
                }

                var order = 0;

                foreach (var option in set)
                {
                    if (!AzerbaijaniText.IsSlug(option.Value))
                    {
                        throw new InvalidOperationException(
                            $"Option value '{option.Value}' in set '{seed.OptionSet}' is not slug-safe.");
                    }

                    definition.Options.Add(new AttributeOption
                    {
                        Value = option.Value,
                        LabelAz = option.LabelAz,
                        LabelRu = option.LabelRu,
                        IsActive = true,
                        SortOrder = order += 10
                    });

                    options++;
                }
            }

            db.AttributeDefinitions.Add(definition);
            present.Add((categoryId, seed.Key));
            definitions++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (definitions, options);
    }

    private async Task<int> SeedDevelopmentRegionsAsync(CancellationToken cancellationToken)
    {
        var file = Load<RegionSeedFile>("regions.dev.json");
        var existing = await db.Regions.Select(r => r.Slug).ToListAsync(cancellationToken);
        var present = existing.ToHashSet();
        var inserted = 0;

        foreach (var seed in file.Regions)
        {
            if (!present.Add(seed.Slug))
            {
                continue;
            }

            db.Regions.Add(new Region
            {
                Slug = seed.Slug,
                NameAz = seed.NameAz,
                NameRu = seed.NameRu,
                Type = seed.Type,
                Depth = 0,
                Latitude = seed.Latitude,
                Longitude = seed.Longitude,
                SortOrder = seed.SortOrder,
                IsActive = true,
                CreatedAt = clock.UtcNow
            });

            inserted++;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (inserted > 0)
        {
            logger.LogWarning(
                "Seeded {Count} DEVELOPMENT regions. This fixture is not authoritative and must not " +
                "reach production; import the official dataset instead.", inserted);
        }

        return inserted;
    }

    private async Task<int> SeedPagesAsync(CancellationToken cancellationToken)
    {
        var seeds = Load<List<StaticPageSeed>>("pages.json");
        var present = (await db.StaticPages.Select(p => p.Slug).ToListAsync(cancellationToken)).ToHashSet();
        var inserted = 0;

        foreach (var seed in seeds)
        {
            if (!present.Add(seed.Slug))
            {
                continue;
            }

            db.StaticPages.Add(new StaticPage
            {
                Slug = seed.Slug,
                PageType = seed.PageType,
                TitleAz = seed.TitleAz,
                TitleRu = seed.TitleRu,
                BodyAz = seed.BodyAz,
                BodyRu = seed.BodyRu,
                MetaDescriptionAz = seed.MetaDescriptionAz,
                ExcerptAz = seed.ExcerptAz,
                IsPublished = seed.IsPublished,
                PublishedAt = seed.IsPublished ? clock.UtcNow : null,
                SortOrder = seed.SortOrder,
                CreatedAt = clock.UtcNow
            });

            inserted++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return inserted;
    }

    private async Task<(int Categories, int Items)> SeedFaqAsync(CancellationToken cancellationToken)
    {
        var seeds = Load<List<FaqCategorySeed>>("faq.json");
        var existing = await db.FaqCategories.ToDictionaryAsync(c => c.Slug, cancellationToken);
        var categories = 0;
        var items = 0;

        foreach (var seed in seeds)
        {
            var category = existing.GetValueOrDefault(seed.Slug);

            if (category is null)
            {
                category = new FaqCategory
                {
                    Slug = seed.Slug,
                    NameAz = seed.NameAz,
                    NameRu = seed.NameRu,
                    SortOrder = seed.SortOrder,
                    CreatedAt = clock.UtcNow
                };

                db.FaqCategories.Add(category);
                await db.SaveChangesAsync(cancellationToken);
                existing[seed.Slug] = category;
                categories++;
            }

            var questions = await db.FaqItems
                .Where(i => i.FaqCategoryId == category.Id)
                .Select(i => i.QuestionAz)
                .ToListAsync(cancellationToken);

            var present = questions.ToHashSet();

            foreach (var item in seed.Items)
            {
                if (!present.Add(item.QuestionAz))
                {
                    continue;
                }

                db.FaqItems.Add(new FaqItem
                {
                    FaqCategoryId = category.Id,
                    QuestionAz = item.QuestionAz,
                    AnswerAz = item.AnswerAz,
                    QuestionRu = item.QuestionRu,
                    AnswerRu = item.AnswerRu,
                    SortOrder = item.SortOrder,
                    CreatedAt = clock.UtcNow
                });

                items++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (categories, items);
    }

    private static T Load<T>(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = $"Ovcuprim.Infrastructure.Persistence.Seed.Data.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Seed resource '{resource}' was not found.");

        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Seed resource '{resource}' deserialised to null.");
    }
}
