using Microsoft.EntityFrameworkCore;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Application.Abstractions;

/// <summary>
/// The persistence surface the application layer is allowed to touch. Implemented by
/// <c>AppDbContext</c> in Infrastructure, which keeps Application free of a provider reference.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<OtpCode> OtpCodes { get; }

    DbSet<AuditLog> AuditLogs { get; }

    DbSet<Category> Categories { get; }

    DbSet<AttributeDefinition> AttributeDefinitions { get; }

    DbSet<AttributeOption> AttributeOptions { get; }

    DbSet<Region> Regions { get; }

    DbSet<Listing> Listings { get; }

    DbSet<ListingMedia> ListingMedia { get; }

    DbSet<ListingQuota> ListingQuotas { get; }

    DbSet<Favorite> Favorites { get; }

    DbSet<Report> Reports { get; }

    DbSet<ModerationAction> ModerationActions { get; }

    DbSet<Store> Stores { get; }

    DbSet<StoreFollow> StoreFollows { get; }

    DbSet<StaticPage> StaticPages { get; }

    DbSet<FaqCategory> FaqCategories { get; }

    DbSet<FaqItem> FaqItems { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<PromotionPackage> PromotionPackages { get; }

    DbSet<PaymentOrder> PaymentOrders { get; }

    DbSet<PaymentTransaction> PaymentTransactions { get; }

    DbSet<Promotion> Promotions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
