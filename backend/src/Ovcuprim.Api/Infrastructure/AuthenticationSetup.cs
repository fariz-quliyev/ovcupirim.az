using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Ovcuprim.Application.Auth;

namespace Ovcuprim.Api.Infrastructure;

public static class AuthenticationSetup
{
    public static class Policies
    {
        public const string Admin = "Admin";
        public const string Moderator = "Moderator";
    }

    public static class RateLimits
    {
        /// <summary>Sending an SMS costs money — the tightest limit on the API.</summary>
        public const string OtpRequest = "otp-request";

        /// <summary>Guessing a code. Complements the per-code attempt counter.</summary>
        public const string OtpVerify = "otp-verify";

        public const string Auth = "auth";

        /// <summary>Creating listings: generous for a real seller, costly for a scripted flood.</summary>
        public const string ListingCreate = "listing-create";

        /// <summary>Uploads are the most expensive request the API serves.</summary>
        public const string MediaUpload = "media-upload";

        /// <summary>Catalogue and search: generous for a browsing visitor, costly for a scraper.</summary>
        public const string ListingSearch = "listing-search";

        /// <summary>
        /// Revealing a seller's number. The tightest anonymous limit on the API: ShortId is a dense
        /// sequential column, so without this an unauthenticated caller could walk it and harvest
        /// every phone number on the site.
        /// </summary>
        public const string PhoneReveal = "phone-reveal";

        /// <summary>
        /// The public listing page and its "similar" strip. Loose enough for a person clicking
        /// through the site, tight enough that enumerating every listing is not free — and it
        /// bounds how far the view counter can be inflated.
        /// </summary>
        public const string ListingDetail = "listing-detail";

        /// <summary>
        /// Applying for a storefront. Each application creates a row and lands in a human queue,
        /// so the limit is deliberately small.
        /// </summary>
        public const string StoreApply = "store-apply";

        /// <summary>Reports are accepted anonymously, so the limit is what stops abuse.</summary>
        public const string ListingReport = "listing-report";

        /// <summary>Starting a promotion purchase creates a payment order and calls out to the gateway.</summary>
        public const string PromotionOrderCreate = "promotion-order-create";

        /// <summary>
        /// The gateway's own callback. Anonymous, so it partitions by address like every other
        /// anonymous policy — deliberately generous, since a burst of legitimate retries from the
        /// provider must never be the reason a real payment confirmation is dropped.
        /// </summary>
        public const string PaymentCallback = "payment-callback";

        /// <summary>
        /// Mutating administrative actions, partitioned by the operator rather than by address:
        /// an operations team behind one office IP would otherwise share a single bucket. Set
        /// generously — this bounds a compromised or scripted admin token, it does not pace a
        /// person working a queue.
        /// </summary>
        public const string AdminAction = "admin-action";
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Validation reads the very same IOptions<JwtOptions> the token service signs with, so the
        // signing and validating keys can never drift apart.
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                // Startup validation should have caught this; kept as the last line of defence.
                if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < JwtOptions.MinimumSigningKeyLength)
                {
                    throw new InvalidOperationException(
                        $"Auth:Jwt:SigningKey is missing or shorter than {JwtOptions.MinimumSigningKeyLength} characters. " +
                        "Configure it via environment variables or user-secrets.");
                }

                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    // No grace period: an expired access token is expired.
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = ClaimTypes.NameIdentifier,
                    RoleClaimType = ClaimTypes.Role
                };

                // Authentication failures come back as ProblemDetails like every other error.
                bearer.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await WriteProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                            "Unauthorized", "Bu əməliyyat üçün giriş tələb olunur.");
                    },
                    OnForbidden = context => WriteProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden,
                        "Forbidden", "Bu əməliyyat üçün icazəniz yoxdur.")
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Admin, policy => policy.RequireRole("Admin"))
            .AddPolicy(Policies.Moderator, policy => policy.RequireRole("Admin", "Moderator"));

        return services;
    }

    /// <summary>
    /// The OTP limits.
    /// </summary>
    /// <remarks>
    /// Sending a code costs money and guessing one is an attack, so these are the tightest limits
    /// on the API and the production numbers are the defaults below. They are configurable purely
    /// so the browser tests — which register and sign in many times from one address — are not
    /// throttled by a control aimed at the public internet. Production configures nothing and
    /// therefore gets exactly the limits it had before.
    /// </remarks>
    public sealed class OtpRateLimitOptions
    {
        public const string SectionName = "Auth:RateLimits:Otp";

        public int RequestPermitLimit { get; set; } = 5;

        public int VerifyPermitLimit { get; set; } = 10;

        public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(15);
    }

    /// <summary>
    /// One fixed window, for one policy. Bound as a named option under
    /// <c>Auth:RateLimits:{name}</c>, where the names are the ones in <see cref="WindowPolicies"/>.
    /// </summary>
    public sealed class WindowRateLimitOptions
    {
        public const string SectionPrefix = "Auth:RateLimits";

        public int PermitLimit { get; set; }

        public TimeSpan Window { get; set; }
    }

    /// <summary>
    /// Every fixed window on the API, with the production numbers that were always here.
    /// </summary>
    /// <remarks>
    /// These are aimed at the public internet, where one address is roughly one person. A browser
    /// test suite is the opposite: a dozen registrations, listings and moderation decisions a
    /// minute, all from a single address, which trips controls that a real visitor would never
    /// come near. So each window is configurable and each default below is the production value —
    /// production configures nothing and gets exactly the limits it had before. Keeping them in one
    /// table rather than eleven near-identical registrations also means the numbers can be read at
    /// a glance, which is the only way anyone will notice one drifting.
    ///
    /// The OTP windows are not here: sending and guessing a code share one window but have separate
    /// permits, so they keep their own options type.
    /// </remarks>
    private static readonly (string Name, string Policy, bool PerOperator, int PermitLimit, TimeSpan Window)[] WindowPolicies =
    [
        // Session restore happens on every full page load, and behind a carrier NAT a whole pool of
        // people share one partition key.
        ("Session", RateLimits.Auth, false, 30, TimeSpan.FromMinutes(1)),
        ("ListingCreate", RateLimits.ListingCreate, false, 20, TimeSpan.FromMinutes(10)),
        ("MediaUpload", RateLimits.MediaUpload, false, 60, TimeSpan.FromMinutes(10)),
        ("ListingSearch", RateLimits.ListingSearch, false, 300, TimeSpan.FromMinutes(1)),
        // A buyer reveals a handful of numbers in a session; a harvester wants thousands.
        ("PhoneReveal", RateLimits.PhoneReveal, false, 20, TimeSpan.FromHours(1)),
        ("ListingDetail", RateLimits.ListingDetail, false, 120, TimeSpan.FromMinutes(1)),
        ("StoreApply", RateLimits.StoreApply, false, 5, TimeSpan.FromDays(1)),
        ("ListingReport", RateLimits.ListingReport, false, 10, TimeSpan.FromMinutes(10)),
        // Each purchase attempt calls out to the gateway; generous enough for a seller retrying a
        // declined card, tight enough that a script cannot spin up unlimited pending orders.
        ("PromotionOrderCreate", RateLimits.PromotionOrderCreate, false, 20, TimeSpan.FromMinutes(10)),
        // The gateway may legitimately retry a callback several times against one address (its own).
        // This bounds a runaway retry storm without being the reason a real delivery is dropped.
        ("PaymentCallback", RateLimits.PaymentCallback, false, 120, TimeSpan.FromMinutes(1)),
        // Partitioned by operator rather than address: a shared office address must not throttle
        // one moderator because another is working.
        ("AdminAction", RateLimits.AdminAction, true, 300, TimeSpan.FromMinutes(1)),
    ];

    public static IServiceCollection AddAuthRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Bound as options rather than read here: configuration read at registration time cannot be
        // overridden by a test host, whose configuration sources are applied when the host is built.
        services.Configure<OtpRateLimitOptions>(configuration.GetSection(OtpRateLimitOptions.SectionName));

        foreach (var (name, _, _, permitLimit, window) in WindowPolicies)
        {
            // The production number first, then configuration over the top of it: an environment
            // that sets only PermitLimit keeps the default window rather than silently getting zero.
            services.Configure<WindowRateLimitOptions>(name, limits =>
            {
                limits.PermitLimit = permitLimit;
                limits.Window = window;
            });

            services.Configure<WindowRateLimitOptions>(
                name, configuration.GetSection($"{WindowRateLimitOptions.SectionPrefix}:{name}"));
        }

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // The floor under every endpoint that declares no policy of its own — the anonymous
            // taxonomy reads, chiefly. Those are cheap and publicly cacheable, so a CDN normally
            // absorbs them; this is what stands between a direct-to-origin caller and an unbounded
            // request rate. Deliberately looser than every named policy, so it never becomes the
            // binding constraint on an endpoint that already has one: a metered request has to
            // pass both, and this is only ever the backstop.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 600,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                await WriteProblemAsync(context.HttpContext, StatusCodes.Status429TooManyRequests,
                    "Too many requests", "Çox sayda sorğu göndərildi. Bir az sonra yenidən cəhd edin.");
                _ = cancellationToken;
            };

            options.AddPolicy(RateLimits.OtpRequest, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = OtpLimits(context).RequestPermitLimit,
                    Window = OtpLimits(context).Window,
                    QueueLimit = 0
                }));

            options.AddPolicy(RateLimits.OtpVerify, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = OtpLimits(context).VerifyPermitLimit,
                    Window = OtpLimits(context).Window,
                    QueueLimit = 0
                }));

            foreach (var (name, policy, perOperator, _, _) in WindowPolicies)
            {
                options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
                    perOperator ? OperatorKey(context) : ClientKey(context),
                    _ =>
                    {
                        var limits = WindowLimits(context, name);

                        return new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limits.PermitLimit,
                            Window = limits.Window,
                            QueueLimit = 0
                        };
                    }));
            }
        });

        return services;
    }

    /// <summary>Resolved per request, so the configured values are the built host's.</summary>
    private static OtpRateLimitOptions OtpLimits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<OtpRateLimitOptions>>().Value;

    private static WindowRateLimitOptions WindowLimits(HttpContext context, string name) =>
        context.RequestServices.GetRequiredService<IOptionsMonitor<WindowRateLimitOptions>>().Get(name);

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// The signed-in operator, falling back to the address when the request is not authenticated —
    /// which for an admin route means it is about to be refused anyway. Reading the identity here
    /// is why the limiter runs after authentication.
    /// </summary>
    private static string OperatorKey(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } id
            ? $"user:{id}"
            : ClientKey(context);

    private static async Task WriteProblemAsync(HttpContext context, int status, string title, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        // These are written outside the controllers, so they miss the no-store the admin helpers
        // apply. A challenge or a throttle carries nothing sensitive, but "no admin-path response
        // is cacheable" is easier to hold than a list of exceptions to it.
        context.Response.Headers.CacheControl = "no-store";

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonSerializerOptions.Web));
    }
}
