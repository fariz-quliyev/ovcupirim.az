using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Infrastructure.Persistence.Configurations;

public class PromotionPackageConfiguration : IEntityTypeConfiguration<PromotionPackage>
{
    public void Configure(EntityTypeBuilder<PromotionPackage> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code).HasMaxLength(40).IsRequired();
        builder.Property(p => p.NameAz).HasMaxLength(100).IsRequired();
        builder.Property(p => p.DescriptionAz).HasMaxLength(500);
        builder.Property(p => p.Type).HasConversion<short>();
        builder.Property(p => p.PriceAzn).HasPrecision(12, 2);
        builder.Property(p => p.Currency).HasMaxLength(3).HasDefaultValue("AZN").IsFixedLength();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PromotionPackages_Price_NonNegative", "\"PriceAzn\" >= 0");
            t.HasCheckConstraint("CK_PromotionPackages_Duration_Positive", "\"DurationDays\" > 0");
        });

        builder.HasIndex(p => p.Code).IsUnique();
        builder.HasIndex(p => new { p.IsActive, p.SortOrder });
    }
}

public class PaymentOrderConfiguration : IEntityTypeConfiguration<PaymentOrder>
{
    public void Configure(EntityTypeBuilder<PaymentOrder> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.AmountAzn).HasPrecision(12, 2);
        builder.Property(o => o.Currency).HasMaxLength(3).HasDefaultValue("AZN").IsFixedLength();
        builder.Property(o => o.Status).HasConversion<short>();
        builder.Property(o => o.Provider).HasMaxLength(20).IsRequired();
        builder.Property(o => o.ProviderOrderReference).HasMaxLength(100);
        builder.Property(o => o.RefundedAmountAzn).HasPrecision(12, 2).HasDefaultValue(0m);
        builder.Property(o => o.DurationDays).HasDefaultValue(0);

        // App-managed concurrency token, exactly like Listing.Version — guards a webhook and a
        // status poll racing the same order. Declared unconditionally so the in-memory provider
        // enforces it too and the 409 path is covered by the ordinary test suite.
        builder.Property(o => o.Version).IsConcurrencyToken();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PaymentOrders_Amount_NonNegative", "\"AmountAzn\" >= 0");
            // Defense-in-depth for security audit finding B.3: even if application logic ever had a
            // bug, the database itself refuses a refund total that exceeds what was captured.
            t.HasCheckConstraint("CK_PaymentOrders_RefundedAmount_NonNegative", "\"RefundedAmountAzn\" >= 0");
            t.HasCheckConstraint("CK_PaymentOrders_RefundedAmount_NotExceedingAmount", "\"RefundedAmountAzn\" <= \"AmountAzn\"");
        });

        builder.HasIndex(o => new { o.SellerUserId, o.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(o => new { o.ListingId, o.Status });

        // The expiry sweep's own query. Status is included so the sweep never has to scan orders
        // that already left AwaitingPayment.
        builder.HasIndex(o => new { o.Status, o.ExpiresAt });

        // One row per (provider, provider order reference) — the gateway's own id is unique on its
        // side, and this catches a coding mistake creating two local orders for one gateway order
        // before it ever reaches production. Partial: most rows have no reference yet at Created.
        builder.HasIndex(o => new { o.Provider, o.ProviderOrderReference })
            .IsUnique()
            .HasFilter("\"ProviderOrderReference\" IS NOT NULL");

        builder.HasOne(o => o.SellerUser)
            .WithMany()
            .HasForeignKey(o => o.SellerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Listing)
            .WithMany(l => l.PaymentOrders)
            .HasForeignKey(o => o.ListingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.PromotionPackage)
            .WithMany()
            .HasForeignKey(o => o.PromotionPackageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.EventType).HasConversion<short>();
        builder.Property(t => t.ProviderReference).HasMaxLength(100);
        builder.Property(t => t.ProviderStatusRaw).HasMaxLength(40);
        builder.Property(t => t.AmountAzn).HasPrecision(12, 2);
        builder.Property(t => t.PayloadJson).HasColumnType("jsonb");

        // The idempotency guard (docs/payment-integration-design.md, section F): a re-delivered
        // webhook for the same order and gateway reference hits this constraint and is treated as
        // already-processed rather than reapplied — the exact mechanism a legitimate callback retry
        // needs to be a safe no-op. Scoped to CallbackReceived (1) specifically and not to every
        // event type: StatusChecked rows are written on every authoritative re-check, including
        // repeated ones against the same still-pending order (a seller polling, an admin
        // reconciling), and those must remain free to happen more than once.
        builder.HasIndex(t => new { t.PaymentOrderId, t.EventType, t.ProviderReference })
            .IsUnique()
            .HasFilter("\"ProviderReference\" IS NOT NULL AND \"EventType\" = 1")
            .HasDatabaseName("IX_PaymentTransactions_CallbackIdempotency");

        builder.HasIndex(t => t.CreatedAt);

        builder.HasOne(t => t.PaymentOrder)
            .WithMany(o => o.Transactions)
            .HasForeignKey(t => t.PaymentOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Status).HasConversion<short>();
        builder.Property(p => p.ReversedReason).HasMaxLength(500);

        builder.HasIndex(p => new { p.ListingId, p.CreatedAt }).IsDescending(false, true);

        // Approved rule: one active promotion per listing, no stacking in this phase. Partial on
        // Status = Active (1) so a listing's expired/reversed history never blocks a fresh purchase.
        builder.HasIndex(p => p.ListingId)
            .IsUnique()
            .HasFilter("\"Status\" = 1")
            .HasDatabaseName("IX_Promotions_ListingId_Active");

        // The expiry sweep's own query.
        builder.HasIndex(p => new { p.Status, p.ExpiresAt });

        // One order funds at most one promotion. Postgres treats each NULL as distinct, so future
        // non-paid grants (PaymentOrderId left null) are never constrained against each other.
        builder.HasIndex(p => p.PaymentOrderId).IsUnique();

        builder.HasOne(p => p.Listing)
            .WithMany(l => l.Promotions)
            .HasForeignKey(p => p.ListingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.PromotionPackage)
            .WithMany(pkg => pkg.Promotions)
            .HasForeignKey(p => p.PromotionPackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.PaymentOrder)
            .WithOne(o => o.Promotion)
            .HasForeignKey<Promotion>(p => p.PaymentOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
