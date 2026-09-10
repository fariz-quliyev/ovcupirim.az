using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Infrastructure.Persistence.Configurations;

public class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Comment).HasMaxLength(1000);
        builder.Property(r => r.Reason).HasConversion<short>();
        builder.Property(r => r.Status).HasConversion<short>();

        builder.HasIndex(r => new { r.Status, r.CreatedAt });
        builder.HasIndex(r => r.ListingId);

        builder.HasOne(r => r.Listing)
            .WithMany(l => l.Reports)
            .HasForeignKey(r => r.ListingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.ReporterUser)
            .WithMany()
            .HasForeignKey(r => r.ReporterUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(r => r.Listing.DeletedAt == null);
    }
}

public class ModerationActionConfiguration : IEntityTypeConfiguration<ModerationAction>
{
    public void Configure(EntityTypeBuilder<ModerationAction> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Reason).HasMaxLength(500);
        builder.Property(a => a.Action).HasConversion<short>();

        builder.HasIndex(a => new { a.ListingId, a.CreatedAt });
        builder.HasIndex(a => new { a.ModeratorUserId, a.CreatedAt });

        builder.HasOne(a => a.Listing)
            .WithMany(l => l.ModerationActions)
            .HasForeignKey(a => a.ListingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.ModeratorUser)
            .WithMany()
            .HasForeignKey(a => a.ModeratorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(a => a.Listing.DeletedAt == null);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.EntityType).HasMaxLength(60).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(60).IsRequired();
        builder.Property(a => a.PayloadJson).HasColumnType("jsonb");
        builder.Property(a => a.IpAddress).HasMaxLength(64);

        builder.HasIndex(a => a.CreatedAt).IsDescending();
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.ActorUserId);
    }
}
