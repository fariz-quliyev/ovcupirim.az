using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Infrastructure.Persistence.Configurations;

public class StaticPageConfiguration : IEntityTypeConfiguration<StaticPage>
{
    public void Configure(EntityTypeBuilder<StaticPage> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Slug).HasMaxLength(80).IsRequired();
        builder.Property(p => p.TitleAz).HasMaxLength(160).IsRequired();
        builder.Property(p => p.TitleRu).HasMaxLength(160);
        builder.Property(p => p.BodyAz).IsRequired();
        builder.Property(p => p.MetaDescriptionAz).HasMaxLength(320);
        builder.Property(p => p.MetaDescriptionRu).HasMaxLength(320);
        builder.Property(p => p.ExcerptAz).HasMaxLength(400);
        builder.Property(p => p.ExcerptRu).HasMaxLength(400);
        builder.Property(p => p.CoverImageKey).HasMaxLength(200);
        builder.Property(p => p.PageType).HasConversion<short>();

        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => new { p.PageType, p.IsPublished, p.SortOrder });
    }
}

public class FaqCategoryConfiguration : IEntityTypeConfiguration<FaqCategory>
{
    public void Configure(EntityTypeBuilder<FaqCategory> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.NameAz).HasMaxLength(120).IsRequired();
        builder.Property(c => c.NameRu).HasMaxLength(120);
        builder.Property(c => c.Slug).HasMaxLength(120).IsRequired();

        builder.HasIndex(c => c.Slug).IsUnique();
    }
}

public class FaqItemConfiguration : IEntityTypeConfiguration<FaqItem>
{
    public void Configure(EntityTypeBuilder<FaqItem> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.QuestionAz).HasMaxLength(300).IsRequired();
        builder.Property(i => i.QuestionRu).HasMaxLength(300);
        builder.Property(i => i.AnswerAz).IsRequired();

        builder.HasIndex(i => new { i.FaqCategoryId, i.SortOrder });

        builder.HasOne(i => i.FaqCategory)
            .WithMany(c => c.Items)
            .HasForeignKey(i => i.FaqCategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AdInquiryConfiguration : IEntityTypeConfiguration<AdInquiry>
{
    public void Configure(EntityTypeBuilder<AdInquiry> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Company).HasMaxLength(120).IsRequired();
        builder.Property(i => i.Contact).HasMaxLength(160).IsRequired();
        builder.Property(i => i.Note).HasMaxLength(2000);

        builder.HasIndex(i => new { i.IsHandled, i.CreatedAt });
    }
}
