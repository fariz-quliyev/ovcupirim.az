using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Infrastructure.Persistence.Configurations;

public class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Slug).HasMaxLength(120).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(2000);
        builder.Property(s => s.Address).HasMaxLength(200);
        builder.Property(s => s.Phone).HasMaxLength(20);
        builder.Property(s => s.Status).HasConversion<short>();
        builder.Property(s => s.LogoStorageKey).HasMaxLength(200);
        builder.Property(s => s.BannerStorageKey).HasMaxLength(200);

        // Declared on every provider, so the conflict path is covered by the ordinary test suite.
        builder.Property(s => s.Version).IsConcurrencyToken();

        builder.HasIndex(s => s.Slug).IsUnique();
        builder.HasIndex(s => s.OwnerUserId).IsUnique();

        // The public directory: active storefronts in name order.
        builder.HasIndex(s => new { s.Status, s.Name });

        builder.HasOne(s => s.OwnerUser)
            .WithOne(u => u.Store)
            .HasForeignKey<Store>(s => s.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(s => s.OwnerUser.DeletedAt == null);
    }
}

public class StoreFollowConfiguration : IEntityTypeConfiguration<StoreFollow>
{
    public void Configure(EntityTypeBuilder<StoreFollow> builder)
    {
        builder.HasKey(f => new { f.UserId, f.StoreId });

        builder.HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Store)
            .WithMany(s => s.Followers)
            .HasForeignKey(f => f.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(f => f.User.DeletedAt == null && f.Store.OwnerUser.DeletedAt == null);
    }
}
