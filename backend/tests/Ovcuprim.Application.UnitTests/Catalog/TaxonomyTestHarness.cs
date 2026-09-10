using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Categories;
using Ovcuprim.Application.UnitTests.Auth;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;
using Ovcuprim.Infrastructure.Persistence;
using Ovcuprim.Infrastructure.Persistence.Seed;

namespace Ovcuprim.Application.UnitTests.Catalog;

/// <summary>Pass-through cache so a test always observes the current database state.</summary>
public sealed class NoOpTaxonomyCache : ITaxonomyCache
{
    public int InvalidateCount { get; private set; }

    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default)
        where T : class => factory(cancellationToken);

    public void Invalidate() => InvalidateCount++;
}

public sealed class StubCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; } = Guid.NewGuid();
    public string? Role { get; set; } = "Admin";
    public bool IsAuthenticated => UserId is not null;
    public bool IsInRole(string role) => string.Equals(Role, role, StringComparison.Ordinal);
}

public sealed class TaxonomyTestHarness : IDisposable
{
    public TaxonomyTestHarness()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"taxonomy-{Guid.NewGuid()}")
            .Options;

        Clock = new FakeClock(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));
        Db = new AppDbContext(options, Clock);
        Cache = new NoOpTaxonomyCache();
        CurrentUser = new StubCurrentUser();

        Categories = new CategoryService(Db, Cache);
        Validator = new AttributeValidator();
        Admin = new CategoryAdminService(Db, Categories, Cache, CurrentUser, Clock);
        Seeder = new TaxonomySeeder(Db, Clock, NullLogger<TaxonomySeeder>.Instance);
    }

    public AppDbContext Db { get; }
    public FakeClock Clock { get; }
    public NoOpTaxonomyCache Cache { get; }
    public StubCurrentUser CurrentUser { get; }
    public ICategoryService Categories { get; }
    public IAttributeValidator Validator { get; }
    public ICategoryAdminService Admin { get; }
    public ITaxonomySeeder Seeder { get; }

    public Task<SeedReport> SeedAsync() => Seeder.SeedAsync(includeDevelopmentRegions: true);

    /// <summary>
    /// A compact fixture that exercises inheritance, override and restriction propagation without
    /// depending on the production seed content.
    /// </summary>
    public async Task BuildFixtureAsync()
    {
        var parent = new Category
        {
            Slug = "parent", NameAz = "Parent", Depth = 0, SortOrder = 10,
            RestrictionStatus = RestrictionStatus.Unrestricted, CreatedAt = Clock.UtcNow
        };

        Db.Categories.Add(parent);
        await Db.SaveChangesAsync();

        var child = new Category
        {
            Slug = "child", NameAz = "Child", ParentId = parent.Id, Depth = 1, SortOrder = 10,
            RestrictionStatus = RestrictionStatus.Unrestricted, CreatedAt = Clock.UtcNow
        };

        var sibling = new Category
        {
            Slug = "sibling", NameAz = "Sibling", ParentId = parent.Id, Depth = 1, SortOrder = 20,
            RestrictionStatus = RestrictionStatus.Restricted, CreatedAt = Clock.UtcNow
        };

        Db.Categories.AddRange(child, sibling);
        await Db.SaveChangesAsync();

        // Inherited by descendants.
        Db.AttributeDefinitions.Add(new AttributeDefinition
        {
            CategoryId = parent.Id, Key = "brand_like", LabelAz = "Marka", DataType = AttributeDataType.Text,
            MaxLength = 60, AppliesToDescendants = true, SortOrder = 10, CreatedAt = Clock.UtcNow
        });

        // Not inherited.
        Db.AttributeDefinitions.Add(new AttributeDefinition
        {
            CategoryId = parent.Id, Key = "parent_only", LabelAz = "Yalnız valideyn", DataType = AttributeDataType.Text,
            AppliesToDescendants = false, SortOrder = 20, CreatedAt = Clock.UtcNow
        });

        // Inherited, but overridden by the child below.
        var inheritedSize = new AttributeDefinition
        {
            CategoryId = parent.Id, Key = "size", LabelAz = "Ölçü", DataType = AttributeDataType.Select,
            AppliesToDescendants = true, SortOrder = 30, CreatedAt = Clock.UtcNow
        };
        inheritedSize.Options.Add(new AttributeOption { Value = "s", LabelAz = "S", SortOrder = 10 });
        inheritedSize.Options.Add(new AttributeOption { Value = "m", LabelAz = "M", SortOrder = 20 });
        Db.AttributeDefinitions.Add(inheritedSize);

        var childSize = new AttributeDefinition
        {
            CategoryId = child.Id, Key = "size", LabelAz = "Ölçü", DataType = AttributeDataType.Select,
            IsRequired = true, SortOrder = 30, CreatedAt = Clock.UtcNow
        };
        childSize.Options.Add(new AttributeOption { Value = "40", LabelAz = "40", SortOrder = 10 });
        childSize.Options.Add(new AttributeOption { Value = "41", LabelAz = "41", SortOrder = 20 });
        childSize.Options.Add(new AttributeOption { Value = "42", LabelAz = "42", SortOrder = 30 });
        Db.AttributeDefinitions.Add(childSize);

        Db.AttributeDefinitions.Add(new AttributeDefinition
        {
            CategoryId = child.Id, Key = "length", LabelAz = "Uzunluq", DataType = AttributeDataType.Number,
            Unit = "m", MinValue = 0.5m, MaxValue = 6m, DecimalPlaces = 2, IsRequired = true,
            SortOrder = 40, CreatedAt = Clock.UtcNow
        });

        var features = new AttributeDefinition
        {
            CategoryId = child.Id, Key = "features", LabelAz = "Funksiyalar", DataType = AttributeDataType.MultiSelect,
            SortOrder = 50, CreatedAt = Clock.UtcNow
        };
        features.Options.Add(new AttributeOption { Value = "a", LabelAz = "A", SortOrder = 10 });
        features.Options.Add(new AttributeOption { Value = "b", LabelAz = "B", SortOrder = 20 });
        features.Options.Add(new AttributeOption { Value = "retired", LabelAz = "Köhnə", IsActive = false, SortOrder = 30 });
        Db.AttributeDefinitions.Add(features);

        Db.AttributeDefinitions.Add(new AttributeDefinition
        {
            CategoryId = child.Id, Key = "waterproof", LabelAz = "Su keçirməyən", DataType = AttributeDataType.Boolean,
            SortOrder = 60, CreatedAt = Clock.UtcNow
        });

        Db.AttributeDefinitions.Add(new AttributeDefinition
        {
            CategoryId = child.Id, Key = "retired_attr", LabelAz = "Köhnə", DataType = AttributeDataType.Text,
            IsActive = false, SortOrder = 70, CreatedAt = Clock.UtcNow
        });

        await Db.SaveChangesAsync();
    }

    public void Dispose() => Db.Dispose();
}
