using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.NameAz).HasMaxLength(80).IsRequired();
        builder.Property(c => c.NameRu).HasMaxLength(80);
        builder.Property(c => c.Slug).HasMaxLength(80).IsRequired();
        builder.Property(c => c.PreviousSlug).HasMaxLength(80);
        builder.Property(c => c.MetaTitleAz).HasMaxLength(160);
        builder.Property(c => c.MetaTitleRu).HasMaxLength(160);
        builder.Property(c => c.MetaDescriptionAz).HasMaxLength(320);
        builder.Property(c => c.MetaDescriptionRu).HasMaxLength(320);
        builder.Property(c => c.IconKey).HasMaxLength(40);
        builder.Property(c => c.ImageKey).HasMaxLength(200);
        builder.Property(c => c.RestrictionStatus).HasConversion<short>();

        builder.HasIndex(c => new { c.ParentId, c.Slug }).IsUnique();

        // PostgreSQL treats NULLs as distinct, so the composite index above does NOT stop two
        // top-level categories sharing a slug. This filtered index closes that hole.
        builder.HasIndex(c => c.Slug)
            .IsUnique()
            .HasFilter("\"ParentId\" IS NULL")
            .HasDatabaseName("IX_Categories_RootSlug");

        builder.HasIndex(c => new { c.ParentId, c.SortOrder });
        builder.HasIndex(c => c.IsActive);

        builder.HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AttributeDefinitionConfiguration : IEntityTypeConfiguration<AttributeDefinition>
{
    public void Configure(EntityTypeBuilder<AttributeDefinition> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Key).HasMaxLength(40).IsRequired();
        builder.Property(a => a.LabelAz).HasMaxLength(80).IsRequired();
        builder.Property(a => a.LabelRu).HasMaxLength(80);
        builder.Property(a => a.Unit).HasMaxLength(16);
        builder.Property(a => a.PlaceholderAz).HasMaxLength(80);
        builder.Property(a => a.PlaceholderRu).HasMaxLength(80);
        builder.Property(a => a.HelpTextAz).HasMaxLength(200);
        builder.Property(a => a.HelpTextRu).HasMaxLength(200);
        builder.Property(a => a.MinValue).HasPrecision(18, 4);
        builder.Property(a => a.MaxValue).HasPrecision(18, 4);
        builder.Property(a => a.DataType).HasConversion<short>();

        builder.HasIndex(a => new { a.CategoryId, a.Key }).IsUnique();
        builder.HasIndex(a => new { a.CategoryId, a.SortOrder });

        builder.HasOne(a => a.Category)
            .WithMany(c => c.Attributes)
            .HasForeignKey(a => a.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AttributeOptionConfiguration : IEntityTypeConfiguration<AttributeOption>
{
    public void Configure(EntityTypeBuilder<AttributeOption> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Value).HasMaxLength(80).IsRequired();
        builder.Property(o => o.LabelAz).HasMaxLength(80).IsRequired();
        builder.Property(o => o.LabelRu).HasMaxLength(80);

        builder.HasIndex(o => new { o.AttributeDefinitionId, o.Value }).IsUnique();
        builder.HasIndex(o => new { o.AttributeDefinitionId, o.SortOrder });

        // The value travels in filter query strings, so it must stay URL-safe.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AttributeOptions_Value_Slug",
            "\"Value\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$'"));

        builder.HasOne(o => o.AttributeDefinition)
            .WithMany(a => a.Options)
            .HasForeignKey(o => o.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RegionConfiguration : IEntityTypeConfiguration<Region>
{
    public void Configure(EntityTypeBuilder<Region> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.NameAz).HasMaxLength(80).IsRequired();
        builder.Property(r => r.NameRu).HasMaxLength(80);
        builder.Property(r => r.Slug).HasMaxLength(80).IsRequired();
        builder.Property(r => r.Type).HasConversion<short>();

        builder.HasIndex(r => r.Slug).IsUnique();
        builder.HasIndex(r => new { r.ParentId, r.SortOrder });
        builder.HasIndex(r => r.IsActive);

        // No index on IsSelectable: the table holds roughly eighty rows, so the selectable lookup
        // is a sequential scan either way and an index would only add write cost.

        builder.HasOne(r => r.Parent)
            .WithMany(r => r.Children)
            .HasForeignKey(r => r.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
