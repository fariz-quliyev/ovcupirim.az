using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ovcuprim.Application.Admin;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Categories;
using Ovcuprim.Application.Content;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Listings.Search;
using Ovcuprim.Application.Notifications;
using Ovcuprim.Application.Payments;
using Ovcuprim.Application.Regions;
using Ovcuprim.Application.Stores;
using Ovcuprim.Application.Users;

namespace Ovcuprim.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly(), includeInternalTypes: true);

        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));
        // Same treatment as the peppering key below. The JwtBearer setup also checks this, but that
        // check runs when the authentication handler first resolves its options — on the first
        // request, not at boot — so on its own it is a late failure too.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.SigningKey) && options.SigningKey.Length >= JwtOptions.MinimumSigningKeyLength,
                $"Auth:Jwt:SigningKey must be configured with at least {JwtOptions.MinimumSigningKeyLength} characters. "
                + "Set it through an environment variable or user-secrets — never in source control.")
            .ValidateOnStart();
        // Validated at startup rather than on the first authentication request. The JWT signing key
        // already fails fast; a peppering key that only threw when someone tried to sign in meant a
        // misconfigured host started cleanly and then broke for real users.
        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.HashingKey) && options.HashingKey.Length >= SecurityOptions.MinimumHashingKeyLength,
                $"Auth:Security:HashingKey must be configured with at least {SecurityOptions.MinimumHashingKeyLength} characters. "
                + "Set it through an environment variable or user-secrets — never in source control.")
            .ValidateOnStart();

        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();

        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ICategoryAdminService, CategoryAdminService>();
        services.AddScoped<IRegionService, RegionService>();
        services.AddScoped<IContentService, ContentService>();
        services.AddSingleton<IAttributeValidator, AttributeValidator>();

        services.AddScoped<IListingService, ListingService>();
        services.AddScoped<IListingPublishService, ListingPublishService>();
        services.AddScoped<IListingMediaService, ListingMediaService>();
        services.AddScoped<IListingModerationService, ListingModerationService>();
        services.AddScoped<IListingQuotaService, ListingQuotaService>();
        services.AddSingleton<IListingQuotaPolicy, ConfigurationListingQuotaPolicy>();

        // Screening: a policy-driven pipeline (IScreeningPolicy, run by CompositeContentScreener),
        // isolated configuration under Listings:Screening. Duplicate detection ships live and
        // flag-only; a prohibited-item policy is deliberately absent (docs/screening-blockers.md) —
        // that is legal content this repository is not authorised to invent, not mechanism.
        services.Configure<ScreeningOptions>(configuration.GetSection(ScreeningOptions.SectionName));
        services.AddScoped<IScreeningPolicy, DuplicateListingScreeningPolicy>();
        services.AddScoped<IContentScreener, CompositeContentScreener>();

        services.AddScoped<IListingQueryParser, ListingQueryParser>();
        services.AddScoped<IListingSearchService, ListingSearchService>();
        services.AddScoped<IFavoriteService, FavoriteService>();
        services.AddScoped<IListingReportService, ListingReportService>();

        // In-app is the only channel today; a push/email/SMS channel is a second INotificationChannel
        // registered alongside it, which NotificationService fans every message out to unchanged.
        services.AddScoped<INotificationChannel, InAppNotificationChannel>();
        services.AddScoped<INotificationService, NotificationService>();

        services.AddScoped<IAdminOverviewService, AdminOverviewService>();
        services.AddScoped<IAdminAuditService, AdminAuditService>();

        services.AddScoped<IStoreService, StoreService>();
        services.AddScoped<IStoreAdminService, StoreAdminService>();
        services.AddScoped<IStoreMediaService, StoreMediaService>();
        services.AddScoped<IStoreFollowService, StoreFollowService>();

        services.Configure<PaymentOptions>(configuration.GetSection(PaymentOptions.SectionName));
        services.AddScoped<IPromotionPackageService, PromotionPackageService>();
        services.AddScoped<IPromotionPackageAdminService, PromotionPackageAdminService>();
        services.AddScoped<IPromotionOrderService, PromotionOrderService>();
        services.AddScoped<IPaymentCallbackService, PaymentCallbackService>();
        services.AddScoped<IPaymentAdminService, PaymentAdminService>();
        services.AddScoped<IPromotionRecoveryService, PromotionRecoveryService>();

        // One buffer for the whole process: views are counted in memory and flushed by the
        // maintenance pass rather than written on the request thread.
        services.AddSingleton<IViewCountBuffer, ViewCountBuffer>();

        return services;
    }
}
