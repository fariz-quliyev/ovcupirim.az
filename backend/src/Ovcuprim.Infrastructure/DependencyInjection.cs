using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Payments;
using Ovcuprim.Infrastructure.Caching;
using Ovcuprim.Infrastructure.Imaging;
using Ovcuprim.Infrastructure.Persistence;
using Ovcuprim.Infrastructure.Persistence.Search;
using Ovcuprim.Infrastructure.Payments;
using Ovcuprim.Infrastructure.Persistence.Seed;
using Ovcuprim.Infrastructure.Security;
using Ovcuprim.Infrastructure.Services;
using Ovcuprim.Infrastructure.Sms;
using Ovcuprim.Infrastructure.Storage;

namespace Ovcuprim.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        var connectionString = configuration.GetConnectionString("Default");

        // Blank counts as absent: an empty value passes a null check and then fails at the first
        // query instead of at boot.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'Default' is not configured. Set ConnectionStrings__Default (see .env.example).");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.EnableRetryOnFailure(3);
            }));

        services.Configure<LocalFileStorageOptions>(configuration.GetSection(LocalFileStorageOptions.SectionName));

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ITaxonomySeeder, TaxonomySeeder>();
        services.AddScoped<IListingSearchStore, ListingSearchStore>();

        // Development tooling: the caller gates it on the environment, never the container.
        services.AddScoped<ISyntheticListingGenerator, SyntheticListingGenerator>();

        services.AddMemoryCache();
        services.AddSingleton<ITaxonomyCache, TaxonomyCache>();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IImageProcessor, ImageSharpProcessor>();
        services.AddSingleton<ISecretHasher, SecretHasher>();
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        // The Development sender writes the code to the log and lets an end-to-end test read it
        // back. Registering it everywhere would mean a production host silently delivering nothing
        // while printing one-time codes into the log, so outside Development the placeholder throws
        // until a real gateway is wired in.
        if (isDevelopment)
        {
            services.AddSingleton<DevelopmentSmsSender>();
            services.AddSingleton<ISmsSender>(sp => sp.GetRequiredService<DevelopmentSmsSender>());
            services.AddSingleton<IOtpProbe>(sp => sp.GetRequiredService<DevelopmentSmsSender>());
        }
        else
        {
            var poctgoyercini = configuration.GetSection(PoctgoyerciniSmsOptions.SectionName);

            // Opt-in, not automatic: which gateway account these credentials belong to — Bumer.az's
            // existing one, or a separate one obtained for OvcuPrim — is still an open question with
            // the provider (see docs/sms-poctgoyercini.md). Wiring this in unconditionally the moment
            // the code exists would presume that question answered. Instead, the safe placeholder
            // that refuses to start sending stays the default until an operator deliberately supplies
            // both values for whichever account they have actually decided to use.
            if (!string.IsNullOrWhiteSpace(poctgoyercini["Username"]) && !string.IsNullOrWhiteSpace(poctgoyercini["Password"]))
            {
                services.Configure<PoctgoyerciniSmsOptions>(poctgoyercini);
                services.AddHttpClient(PoctgoyerciniSmsSender.HttpClientName, client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(20);
                });
                services.AddSingleton<ISmsSender, PoctgoyerciniSmsSender>();
            }
            else
            {
                services.AddSingleton<ISmsSender, UnconfiguredSmsSender>();
            }
        }

        // Payment gateway: the same opt-in shape as the SMS gateway above. Development gets a fake
        // that never calls a real network endpoint and never sees a real credential — every other
        // host requires both keys configured or refuses to start sending, exactly like the SMS
        // placeholder. See docs/payments-epoint.md and docs/payment-integration-design.md.
        if (isDevelopment)
        {
            services.AddSingleton<DevelopmentPaymentGatewayClient>();
            services.AddSingleton<IPaymentGatewayClient>(sp => sp.GetRequiredService<DevelopmentPaymentGatewayClient>());
            services.AddSingleton<IPaymentGatewaySimulator>(sp => sp.GetRequiredService<DevelopmentPaymentGatewayClient>());
        }
        else
        {
            var epoint = configuration.GetSection(EpointGatewayOptions.SectionName);

            if (!string.IsNullOrWhiteSpace(epoint["PublicKey"]) && !string.IsNullOrWhiteSpace(epoint["PrivateKey"]))
            {
                // Integration audit M-2: every order sends the gateway browser-return URLs built from
                // Payments:FrontendBaseUrl. appsettings.json ships it blank on purpose (a deployment
                // fact, like AllowedHosts), and a blank value would hand Epoint a relative path — so a
                // host that has real gateway keys refuses to start without a real absolute origin,
                // the same way it does for AllowedHosts, the secrets and the storage root.
                var frontendBaseUrl = configuration[$"{PaymentOptions.SectionName}:{nameof(PaymentOptions.FrontendBaseUrl)}"];

                if (!Uri.TryCreate(frontendBaseUrl, UriKind.Absolute, out var frontendOrigin)
                    || (frontendOrigin.Scheme != Uri.UriSchemeHttps && frontendOrigin.Scheme != Uri.UriSchemeHttp))
                {
                    throw new InvalidOperationException(
                        "Payments:FrontendBaseUrl must be an absolute http(s) origin (e.g. https://ovcupirim.az) when the "
                        + "Epoint gateway is configured. Set Payments__FrontendBaseUrl — see docs/payments-epoint.md.");
                }

                services.Configure<EpointGatewayOptions>(epoint);
                services.AddHttpClient(EpointPaymentGatewayClient.HttpClientName, (sp, client) =>
                {
                    var settings = sp.GetRequiredService<IOptions<EpointGatewayOptions>>().Value;
                    client.BaseAddress = new Uri(settings.BaseUrl);
                    client.Timeout = TimeSpan.FromSeconds(20);
                });
                services.AddSingleton<IPaymentGatewayClient, EpointPaymentGatewayClient>();
            }
            else
            {
                services.AddSingleton<IPaymentGatewayClient, UnconfiguredPaymentGatewayClient>();
            }
        }

        return services;
    }
}
