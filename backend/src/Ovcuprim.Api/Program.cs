using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Listings.Search;
using Ovcuprim.Application.Payments;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;
using Ovcuprim.Infrastructure;
using Ovcuprim.Infrastructure.Payments;
using Ovcuprim.Infrastructure.Persistence;
using Ovcuprim.Infrastructure.Persistence.Seed;
using Ovcuprim.Infrastructure.Sms;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicy = "OvcupirimFrontend";

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddApplication(builder.Configuration);

// Which SMS/payment gateway gets wired in — real or simulated — not the strict deployment checks
// below (host filtering, storage, forwarded headers, HSTS all still gate on IsDevelopment() alone,
// unchanged). Staging is otherwise production-shaped end to end; the one thing it needs that a
// true production host must never have is a way to complete an OTP sign-in and a payment without
// a real SMS/Epoint account, since neither has a confirmed sandbox yet (see
// docs/payments-epoint.md, docs/sms-poctgoyercini.md). The simulated implementations this unlocks
// are the same ones Development already uses and are exercised by the full existing test suite;
// the diagnostic endpoints that read an OTP or drive a fake checkout are mapped below under this
// same condition, but never reachable through the public reverse proxy — see frontend/nginx.conf.
var allowSimulatedIntegrations = builder.Environment.IsDevelopment() || builder.Environment.IsStaging();
builder.Services.AddInfrastructure(builder.Configuration, allowSimulatedIntegrations);

builder.Services.AddJwtAuthentication();
builder.Services.AddAuthRateLimiting(builder.Configuration);

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ValidationFilter>();

    // Route tokens resolve lowercase, so /api/v1/listings is the advertised spelling as well as
    // the working one.
    options.Conventions.Add(new RouteTokenTransformerConvention(new LowercaseRouteTransformer()));
});

// Post-configure: MVC adds its formatters after AddControllers runs, and without this the JSON
// formatter only advertises application/json, so every ProblemDetails response is negotiated
// down to the wrong content type.
builder.Services.PostConfigure<MvcOptions>(options =>
{
    foreach (var formatter in options.OutputFormatters.OfType<SystemTextJsonOutputFormatter>())
    {
        formatter.SupportedMediaTypes.Add("application/problem+json");
    }
});
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Instance = context.HttpContext.Request.Path);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHostedService<ListingMaintenanceService>();
builder.Services.AddHostedService<PromotionMaintenanceService>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

// Explicit allowlist — never a wildcard, because the frontend sends credentials.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// Refuses to start when the API would answer to any Host header. Checked before Build() so a
// misconfigured production host fails immediately rather than serving on a name it does not own.
var allowedHosts = HostFilteringSetup.Validate(
    builder.Configuration[HostFilteringSetup.SectionName],
    builder.Environment.IsDevelopment());

// Refuses to start on an unwritable or non-persistent uploads path — see StorageSetup's remarks
// for why a relative path outside Development is refused rather than silently resolved.
StorageSetup.Validate(
    builder.Configuration[$"{StorageSetup.SectionName}:RootPath"],
    builder.Environment.IsDevelopment());

var app = builder.Build();

if (allowedHosts.Count > 0)
{
    app.Logger.LogInformation("Host filtering is active for {Hosts}.", string.Join(", ", allowedHosts));
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

// Which proxies may speak for a caller is a fact about the deployment, so it comes from
// configuration and is never guessed here. Every rate limit partitions on the resulting address.
app.UseForwardedHeaders(ForwardedHeadersSetup.BuildOptions(
    app.Configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>()
        ?? new ForwardedHeadersSettings(),
    app.Environment.IsDevelopment(),
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ForwardedHeaders")));

// Staging shares this whole block with Development — see allowSimulatedIntegrations above for
// why, and frontend/nginx.conf for how the /api/v1/dev/* routes stay off the public internet even
// though the API itself maps them here. A real production host takes neither branch: IsStaging()
// is false there, exactly like IsDevelopment().
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Ovcupirim API"));

    // Reads back the code the Development/Staging sender captured, so a browser test — or a
    // staging verification pass with no real SMS account — can complete a real sign-in without a
    // gateway. Mapped inside this block and nowhere else: outside it the route does not exist,
    // and IOtpProbe is not even registered.
    app.MapGet("/api/v1/dev/otp/{phoneNumber}", (string phoneNumber, IOtpProbe probe) =>
    {
        var code = probe.LastCodeFor(phoneNumber);

        return code is null ? Results.NotFound() : Results.Ok(new { code });
    });

    // Stands in for the bank's own hosted checkout page — see DevelopmentPaymentGatewayClient.
    // Mapped only here, and IPaymentGatewaySimulator is not even registered outside this block.
    app.MapGet("/api/v1/dev/payments/{orderId:guid}/checkout", (Guid orderId) => Results.Content(
        $"""
        <!doctype html>
        <html lang="az"><head><meta charset="utf-8"><title>Development checkout</title></head>
        <body>
        <h1>Development checkout — sifariş {orderId}</h1>
        <p>This page stands in for the bank's hosted checkout. It exists only in Development or Staging.</p>
        <form method="post" action="/api/v1/dev/payments/{orderId}/simulate?outcome=success">
          <button type="submit" data-testid="dev-payment-succeed">Simulate successful payment</button>
        </form>
        <form method="post" action="/api/v1/dev/payments/{orderId}/simulate?outcome=failure">
          <button type="submit" data-testid="dev-payment-fail">Simulate failed payment</button>
        </form>
        </body></html>
        """,
        "text/html"));

    // Drives a real callback through the real pipeline — signature verification, idempotency, the
    // authoritative status re-check, activation — exactly like a production delivery would.
    app.MapPost("/api/v1/dev/payments/{orderId:guid}/simulate", async (
        Guid orderId,
        string outcome,
        IPaymentGatewaySimulator simulator,
        IPaymentCallbackService callbacks,
        IOptions<PaymentOptions> paymentOptions) =>
    {
        var succeeded = string.Equals(outcome, "success", StringComparison.OrdinalIgnoreCase);
        var fields = simulator.BuildCallback(orderId.ToString(), succeeded);
        await callbacks.ProcessCallbackAsync(fields);

        var baseUrl = paymentOptions.Value.FrontendBaseUrl.TrimEnd('/');
        var returnOutcome = succeeded ? "success" : "error";

        return Results.Redirect($"{baseUrl}/promotions/orders/{orderId}/return?outcome={returnOutcome}");
    });

    // Forces an unpaid order past its expiry without waiting for the sweep — E2E-only, so the
    // "expired order can never activate a promotion" rule can be tested in seconds.
    app.MapPost("/api/v1/dev/payments/{orderId:guid}/expire", async (Guid orderId, IAppDbContext db) =>
    {
        var order = await db.PaymentOrders.FirstOrDefaultAsync(o => o.Id == orderId);

        if (order is null)
        {
            return Results.NotFound();
        }

        if (order.Status is PaymentOrderStatus.Created or PaymentOrderStatus.AwaitingPayment)
        {
            order.Status = PaymentOrderStatus.Expired;
            await db.SaveChangesAsync();
        }

        return Results.NoContent();
    });
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Locally uploaded media is served straight from wwwroot; production serves it from the CDN.
app.UseStaticFiles();

app.UseCors(CorsPolicy);

app.UseAuthentication();

// After authentication, so an administrative policy can partition on the operator rather than on
// the address they share with their colleagues. Anonymous policies are unaffected: they key on the
// connection address either way, and authentication does not change that for an anonymous caller.
app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// `dotnet run -- --seed` inserts any missing taxonomy and content, then exits. The seeder is
// idempotent, so running it against a populated database changes nothing.
if (args.Contains("--seed", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<ITaxonomySeeder>();
    var report = await seeder.SeedAsync(includeDevelopmentRegions: app.Environment.IsDevelopment());

    Console.WriteLine(
        $"Seed inserted {report.Total} rows — categories {report.Categories}, " +
        $"attribute definitions {report.AttributeDefinitions}, options {report.AttributeOptions}, " +
        $"regions {report.Regions}, pages {report.StaticPages}, " +
        $"FAQ categories {report.FaqCategories}, FAQ items {report.FaqItems}.");

    return;
}

// `dotnet run -- --set-admin-password` sets or resets the administrator password, then exits.
//
// This is the bootstrap: the panel is reached by password, so the first password cannot itself be
// set through the panel. It is also the only recovery path — an administrator who forgets their
// password has no SMS reset to fall back on, by design.
//
// Both values are read from stdin, never from arguments. An argument would survive in the shell
// history, in the process list while it runs, and in `docker inspect` afterwards. Reaching this
// command at all requires a shell on the host, which already implies database access, so it grants
// nothing that was not already available — it just does it correctly.
if (args.Contains("--set-admin-password", StringComparer.OrdinalIgnoreCase))
{
    // stderr, not stdout: stdout may be piped somewhere that treats it as data.
    await Console.Error.WriteLineAsync(
        "stdin: 1) telefon nömrəsi  2) parol  3) ad və soyad (yalnız yeni hesab yaradılarkən).");

    var phoneInput = await Console.In.ReadLineAsync();
    var password = await Console.In.ReadLineAsync();
    var fullName = (await Console.In.ReadLineAsync())?.Trim();

    var phone = Ovcuprim.Application.Common.PhoneNumber.Normalize(phoneInput ?? string.Empty);

    if (phone is null)
    {
        await Console.Error.WriteLineAsync("Telefon nömrəsi düzgün deyil. Nümunə: +994501234567");
        return;
    }

    if (string.IsNullOrEmpty(password) || password.Length < AdminLoginOptions.MinimumPasswordLength)
    {
        await Console.Error.WriteLineAsync(
            $"Parol ən azı {AdminLoginOptions.MinimumPasswordLength} simvol olmalıdır.");
        return;
    }

    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var timeProvider = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
    var now = timeProvider.UtcNow;

    // Past the query filter: a soft-deleted row still holds the phone number, and silently creating
    // a second account on the same number would fail on the unique index anyway.
    var account = await database.Users
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(u => u.PhoneNumber == phone);

    var created = account is null;

    if (account is null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            await Console.Error.WriteLineAsync("Yeni hesab üçün üçüncü sətirdə ad və soyad tələb olunur.");
            return;
        }

        account = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            FullName = fullName,
            CreatedAt = now
        };

        database.Users.Add(account);
    }

    // Promoting an existing account is deliberate and announced below, not silent: the operator
    // may well be resetting the password of an account that is already the administrator.
    var promoted = account.Role != UserRole.Admin;

    account.Role = UserRole.Admin;
    account.Status = UserStatus.Active;
    account.DeletedAt = null;
    account.PasswordHash = passwordHasher.Hash(password);
    account.FailedLoginAttempts = 0;
    account.LockedUntil = null;

    // An administrator does not sign in by SMS, so this flag can never be set the usual way. The
    // number is verified by the operator standing at the console, which is a stronger check.
    account.IsPhoneVerified = true;

    // Any session that existed under the old password ends here.
    var sessions = await database.RefreshTokens
        .Where(t => t.UserId == account.Id && t.RevokedAt == null)
        .ToListAsync();

    foreach (var session in sessions)
    {
        session.RevokedAt = now;
    }

    database.AuditLogs.Add(new AuditLog
    {
        Id = Guid.NewGuid(),
        ActorUserId = account.Id,
        EntityType = nameof(User),
        EntityId = account.Id.ToString(),
        Action = created ? "AdminAccountCreatedFromConsole" : "AdminPasswordResetFromConsole",
        PayloadJson = Ovcuprim.Application.Common.AuditPayload.From(new { promoted, revokedSessions = sessions.Count }),
        CreatedAt = now
    });

    await database.SaveChangesAsync();

    // Masked: this line ends up in a terminal scrollback and possibly a deployment log.
    await Console.Error.WriteLineAsync(
        $"{(created ? "Administrator hesabı yaradıldı" : "Parol yeniləndi")}: "
        + $"{Ovcuprim.Application.Common.PhoneNumber.Mask(phone)}"
        + $"{(promoted && !created ? " (hesab Admin roluna yüksəldildi)" : string.Empty)}"
        + $", {sessions.Count} aktiv sessiya bağlandı.");

    return;
}

// Development-only performance corpus. Guarded on the environment rather than on a flag alone,
// so a production host refuses even if the argument is passed by accident.
if (args.Contains("--seed-listings", StringComparer.OrdinalIgnoreCase)
    || args.Contains("--purge-listings", StringComparer.OrdinalIgnoreCase))
{
    if (!app.Environment.IsDevelopment())
    {
        Console.Error.WriteLine("Synthetic listing data is development-only and was refused.");
        return;
    }

    using var scope = app.Services.CreateScope();
    var generator = scope.ServiceProvider.GetRequiredService<ISyntheticListingGenerator>();

    // Both paths refresh the denormalised counters afterwards: a generator that leaves the
    // category tree claiming listings that no longer exist is worse than no generator at all.
    var searchStore = scope.ServiceProvider.GetRequiredService<IListingSearchStore>();

    if (args.Contains("--purge-listings", StringComparer.OrdinalIgnoreCase))
    {
        var purged = await generator.PurgeAsync();
        await searchStore.RefreshListingCountsAsync();

        Console.WriteLine($"Purged {purged.Listings} synthetic listings, {purged.Media} media rows and {purged.Users} sellers.");
        return;
    }

    var index = Array.FindIndex(args, a => string.Equals(a, "--seed-listings", StringComparison.OrdinalIgnoreCase));
    var count = index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var parsed) ? parsed : 50_000;

    var report = await generator.GenerateAsync(count);
    await searchStore.RefreshListingCountsAsync();

    Console.WriteLine($"Generated {report.Listings} listings, {report.Media} media rows across {report.Users} synthetic sellers.");

    return;
}

app.Run();

/// <summary>Exposed so the integration test host can reference the entry point.</summary>
public partial class Program;
