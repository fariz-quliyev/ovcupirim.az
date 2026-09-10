using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Infrastructure.Persistence.Configurations;

public class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    public void Configure(EntityTypeBuilder<Listing> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.ShortId).ValueGeneratedOnAdd().UseIdentityAlwaysColumn();
        builder.Property(l => l.Slug).HasMaxLength(120).IsRequired();
        builder.Property(l => l.Title).HasMaxLength(70).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(3000).IsRequired();
        builder.Property(l => l.Price).HasPrecision(12, 2);
        builder.Property(l => l.Currency).HasMaxLength(3).HasDefaultValue("AZN").IsFixedLength();
        builder.Property(l => l.Brand).HasMaxLength(60);
        builder.Property(l => l.ContactPhone).HasMaxLength(20).IsRequired();
        builder.Property(l => l.RejectionReason).HasMaxLength(500);
        builder.Property(l => l.SearchKey).HasMaxLength(200);
        builder.Property(l => l.Condition).HasConversion<short>();
        builder.Property(l => l.SellerType).HasConversion<short>();
        builder.Property(l => l.Status).HasConversion<short>();

        // Declared unconditionally, unlike the xmin mapping it replaced: the in-memory provider
        // honours it too, so the 409 path is covered by the ordinary test suite.
        builder.Property(l => l.Version).IsConcurrencyToken();

        builder.Property(l => l.Attributes)
            .HasColumnType("jsonb")
            .HasConversion(ListingAttributesConverter.Converter, ListingAttributesConverter.Comparer);

        // The generated tsvector column is PostgreSQL-only and is applied in
        // AppDbContext.OnModelCreating, guarded by a provider check.

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Listings_Price_NonNegative", "\"Price\" >= 0");
            t.HasCheckConstraint("CK_Listings_ViewCount_NonNegative", "\"ViewCount\" >= 0");
        });

        builder.HasIndex(l => l.ShortId).IsUnique();
        builder.HasIndex(l => new { l.Status, l.BumpedAt }).IsDescending(false, true);
        builder.HasIndex(l => new { l.CategoryId, l.Status, l.BumpedAt }).IsDescending(false, false, true);
        builder.HasIndex(l => new { l.RegionId, l.Status, l.BumpedAt }).IsDescending(false, false, true);
        builder.HasIndex(l => new { l.Status, l.Price });
        builder.HasIndex(l => new { l.UserId, l.Status });
        builder.HasIndex(l => l.StoreId);
        builder.HasIndex(l => l.ExpiresAt);
        builder.HasIndex(l => l.Attributes).HasMethod("gin").HasOperators("jsonb_path_ops");
        builder.HasIndex(l => l.SearchKey).HasMethod("gin").HasOperators("gin_trgm_ops");

        builder.HasOne(l => l.User)
            .WithMany(u => u.Listings)
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Store)
            .WithMany(s => s.Listings)
            .HasForeignKey(l => l.StoreId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(l => l.Category)
            .WithMany(c => c.Listings)
            .HasForeignKey(l => l.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Region)
            .WithMany(r => r.Listings)
            .HasForeignKey(l => l.RegionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(l => l.DeletedAt == null);
    }
}

public class ListingMediaConfiguration : IEntityTypeConfiguration<ListingMedia>
{
    public void Configure(EntityTypeBuilder<ListingMedia> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.StorageKey).HasMaxLength(200).IsRequired();
        builder.Property(m => m.ContentType).HasMaxLength(60).IsRequired();

        builder.Property(m => m.Variants)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>(),
                new ValueComparer<Dictionary<string, string>>(
                    (a, b) => a != null && b != null && a.Count == b.Count && !a.Except(b).Any(),
                    d => d.Aggregate(0, (acc, kv) => HashCode.Combine(acc, kv.Key.GetHashCode(), kv.Value.GetHashCode())),
                    d => new Dictionary<string, string>(d)));

        builder.HasIndex(m => m.StorageKey).IsUnique();
        builder.HasIndex(m => new { m.ListingId, m.SortOrder }).IsUnique();

        // Exactly one primary image per listing.
        builder.HasIndex(m => m.ListingId)
            .IsUnique()
            .HasFilter("\"IsPrimary\"")
            .HasDatabaseName("IX_ListingMedia_ListingId_Primary");

        builder.HasOne(m => m.Listing)
            .WithMany(l => l.Media)
            .HasForeignKey(m => m.ListingId)
            .OnDelete(DeleteBehavior.Cascade);

        // Matches the parent listing filter so media of a deleted listing never surfaces.
        builder.HasQueryFilter(m => m.Listing.DeletedAt == null);
    }
}

public class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> builder)
    {
        builder.HasKey(f => new { f.UserId, f.ListingId });

        builder.HasIndex(f => new { f.UserId, f.CreatedAt }).IsDescending(false, true);

        builder.HasOne(f => f.User)
            .WithMany(u => u.Favorites)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Listing)
            .WithMany(l => l.Favorites)
            .HasForeignKey(f => f.ListingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(f => f.Listing.DeletedAt == null && f.User.DeletedAt == null);
    }
}

public class ListingQuotaConfiguration : IEntityTypeConfiguration<ListingQuota>
{
    public void Configure(EntityTypeBuilder<ListingQuota> builder)
    {
        builder.HasKey(q => q.Id);

        builder.HasIndex(q => new { q.UserId, q.CategoryId, q.PeriodStart }).IsUnique();

        builder.HasOne(q => q.User)
            .WithMany()
            .HasForeignKey(q => q.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(q => q.Category)
            .WithMany()
            .HasForeignKey(q => q.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(q => q.User.DeletedAt == null);
    }
}
