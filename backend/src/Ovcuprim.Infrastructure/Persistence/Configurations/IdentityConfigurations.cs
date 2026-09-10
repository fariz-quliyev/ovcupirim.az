using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.PhoneNumber).HasMaxLength(20).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(256);
        builder.Property(u => u.FullName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Role).HasConversion<short>();
        builder.Property(u => u.Status).HasConversion<short>();

        builder.HasIndex(u => u.PhoneNumber).IsUnique();
        builder.HasIndex(u => u.Email).IsUnique().HasFilter("\"Email\" IS NOT NULL");

        // Deleted accounts stay in the table for audit and are hidden everywhere else.
        builder.HasQueryFilter(u => u.DeletedAt == null);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.CreatedByIp).HasMaxLength(64);
        builder.Ignore(t => t.IsActive);

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.ExpiresAt });

        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(t => t.User.DeletedAt == null);
    }
}

public class OtpCodeConfiguration : IEntityTypeConfiguration<OtpCode>
{
    public void Configure(EntityTypeBuilder<OtpCode> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.PhoneNumber).HasMaxLength(20).IsRequired();
        builder.Property(o => o.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(o => o.Purpose).HasConversion<short>();

        builder.HasIndex(o => new { o.PhoneNumber, o.ExpiresAt });
    }
}
