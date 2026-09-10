using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Type).HasMaxLength(60).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(1000).IsRequired();
        builder.Property(n => n.EntityType).HasMaxLength(60);
        builder.Property(n => n.EntityId).HasMaxLength(64);

        // "My notifications, newest first" and "how many are unread" are the only two reads this
        // table serves; the index matches the first exactly and covers the second well enough that
        // an unread count never needs a full table scan.
        builder.HasIndex(n => new { n.UserId, n.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(n => new { n.UserId, n.ReadAt });

        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Same convention ListingQuotaConfiguration and the others use: a query filter mirroring the
        // owning User's own soft-delete filter, so a deactivated account's notifications disappear
        // from ordinary reads along with everything else of theirs.
        builder.HasQueryFilter(n => n.User.DeletedAt == null);
    }
}
