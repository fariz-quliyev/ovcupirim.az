using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Domain.Common;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Infrastructure.Persistence;

/// <param name="clock">
/// Injected so timestamps follow the same clock as the application services. Optional to keep
/// design-time tooling (<c>dotnet ef</c>) able to construct the context with options alone.
/// </param>
public class AppDbContext(DbContextOptions<AppDbContext> options, IDateTimeProvider? clock = null)
    : DbContext(options), IAppDbContext
{
    private readonly IDateTimeProvider _clock = clock ?? new SystemClock();

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<AttributeDefinition> AttributeDefinitions => Set<AttributeDefinition>();
    public DbSet<AttributeOption> AttributeOptions => Set<AttributeOption>();
    public DbSet<Region> Regions => Set<Region>();

    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreFollow> StoreFollows => Set<StoreFollow>();

    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingMedia> ListingMedia => Set<ListingMedia>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<ListingQuota> ListingQuotas => Set<ListingQuota>();

    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<StaticPage> StaticPages => Set<StaticPage>();
    public DbSet<FaqCategory> FaqCategories => Set<FaqCategory>();
    public DbSet<FaqItem> FaqItems => Set<FaqItem>();
    public DbSet<AdInquiry> AdInquiries => Set<AdInquiry>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<PromotionPackage> PromotionPackages => Set<PromotionPackage>();
    public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<Promotion> Promotions => Set<Promotion>();

    /// <summary>Weighted full-text document: title (A), brand (B), description (C).</summary>
    private const string SearchVectorSql = """
        setweight(to_tsvector('simple', coalesce("Title", '')), 'A') ||
        setweight(to_tsvector('simple', coalesce("Brand", '')), 'B') ||
        setweight(to_tsvector('simple', coalesce("Description", '')), 'C')
        """;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Fuzzy search over Azerbaijani spelling variants (see plan §13).
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.HasPostgresExtension("unaccent");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // PostgreSQL-only: tsvector has no counterpart in other providers, so the model stays
        // buildable by the in-memory provider used in tests. Migrations always run on Npgsql,
        // so the generated schema is unaffected.
        if (Database.IsNpgsql())
        {
            var listing = modelBuilder.Entity<Listing>();

            listing.Property<NpgsqlTsVector>("SearchVector")
                .HasColumnType("tsvector")
                .HasComputedColumnSql(SearchVectorSql, stored: true);

            listing.HasIndex("SearchVector").HasMethod("gin");
        }

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyTimestamps();
        return base.SaveChanges();
    }

    private void ApplyTimestamps()
    {
        var now = _clock.UtcNow;

        // Concurrency tokens move here and nowhere else. Counter updates run as raw SQL that never
        // touches Version, so viewing a listing or following a store cannot invalidate an edit its
        // owner already has open.
        foreach (var listing in ChangeTracker.Entries<Listing>())
        {
            if (listing.State == EntityState.Modified)
            {
                listing.Entity.Version++;
            }
        }

        foreach (var store in ChangeTracker.Entries<Store>())
        {
            if (store.State == EntityState.Modified)
            {
                store.Entity.Version++;
            }
        }

        foreach (var order in ChangeTracker.Entries<PaymentOrder>())
        {
            if (order.State == EntityState.Modified)
            {
                order.Entity.Version++;
            }
        }

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // A service that set the timestamp deliberately wins over the default.
                    if (entry.Entity.CreatedAt == default)
                    {
                        entry.Entity.CreatedAt = now;
                    }

                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }

    /// <summary>Fallback clock for contexts constructed outside dependency injection.</summary>
    private sealed class SystemClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
