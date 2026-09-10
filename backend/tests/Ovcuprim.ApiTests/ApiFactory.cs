using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;
using Ovcuprim.Application.Listings.Search;
using Ovcuprim.Infrastructure.Persistence;
using Ovcuprim.Infrastructure.Persistence.Seed;

namespace Ovcuprim.Api.IntegrationTests;

/// <summary>
/// A deterministic <see cref="IPaymentGatewayClient"/> double: records every call, lets a test
/// script the next result, and can build genuinely signature-valid (or deliberately forged)
/// callback fields with its own fixed test key — so a test can exercise the real
/// <c>PaymentCallbackController</c>/<c>PaymentCallbackService</c> verification path end to end
/// without a network call or a real gateway.
/// </summary>
public sealed class RecordingPaymentGatewayClient : IPaymentGatewayClient
{
    public const string TestPrivateKey = "api-tests-only-private-key";

    public string ProviderName => "Epoint";

    public List<PaymentGatewayOrderRequest> CreateOrderCalls { get; } = [];

    /// <summary>Every provider reference the authoritative status re-check was asked about, in order.</summary>
    public List<string> StatusCheckCalls { get; } = [];

    public Func<PaymentGatewayOrderRequest, PaymentGatewayOrderResult> NextCreateOrderResult { get; set; } =
        request => new PaymentGatewayOrderResult(true, $"test-{request.OrderId}", $"https://gateway.test/pay/{request.OrderId}", null);

    public Func<string, PaymentGatewayStatusResult> NextStatusResult { get; set; } =
        reference => new PaymentGatewayStatusResult(PaymentGatewayPaymentStatus.Paid, reference, null, "success", "000");

    public Func<string, decimal, string, PaymentGatewayRefundResult> NextRefundResult { get; set; } =
        (reference, _, _) => new PaymentGatewayRefundResult(true, reference, null);

    public Task<PaymentGatewayOrderResult> CreateOrderAsync(
        PaymentGatewayOrderRequest request, CancellationToken cancellationToken = default)
    {
        CreateOrderCalls.Add(request);
        return Task.FromResult(NextCreateOrderResult(request));
    }

    public Task<PaymentGatewayStatusResult> GetOrderStatusAsync(
        string providerOrderReference, CancellationToken cancellationToken = default)
    {
        StatusCheckCalls.Add(providerOrderReference);
        return Task.FromResult(NextStatusResult(providerOrderReference));
    }

    public Task<PaymentGatewayRefundResult> RefundAsync(
        string providerOrderReference, decimal amountAzn, string reason, CancellationToken cancellationToken = default) =>
        Task.FromResult(NextRefundResult(providerOrderReference, amountAzn, reason));

    public PaymentGatewayCallbackResult VerifyCallback(IReadOnlyDictionary<string, string> callbackFields)
    {
        var invalid = new PaymentGatewayCallbackResult(false, null, null, PaymentGatewayPaymentStatus.Unknown, null, null, null);

        if (!callbackFields.TryGetValue("data", out var data) || !callbackFields.TryGetValue("signature", out var signature)
            || !string.Equals(Sign(data), signature, StringComparison.Ordinal))
        {
            return invalid;
        }

        JsonElement payload;

        try
        {
            payload = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(Convert.FromBase64String(data)));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return invalid;
        }

        var orderId = payload.TryGetProperty("order_id", out var o) ? o.GetString() : null;
        var status = payload.TryGetProperty("status", out var s) ? s.GetString() : null;
        var transaction = payload.TryGetProperty("transaction", out var t) ? t.GetString() : null;
        var amount = payload.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDecimal() : (decimal?)null;

        var mapped = status?.Trim().ToLowerInvariant() switch
        {
            "success" => PaymentGatewayPaymentStatus.Paid,
            "failed" => PaymentGatewayPaymentStatus.Failed,
            "returned" => PaymentGatewayPaymentStatus.Refunded,
            _ => PaymentGatewayPaymentStatus.Unknown
        };

        return new PaymentGatewayCallbackResult(true, orderId, transaction, mapped, amount, status, status);
    }

    public static IReadOnlyDictionary<string, string> BuildCallback(
        Guid orderId, bool succeeded, decimal? amount = null, string? transaction = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["order_id"] = orderId.ToString(),
            ["status"] = succeeded ? "success" : "failed",
            ["transaction"] = transaction ?? $"test-{orderId}",
            ["amount"] = amount
        };

        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

        return new Dictionary<string, string> { ["data"] = data, ["signature"] = Sign(data) };
    }

    public static IReadOnlyDictionary<string, string> BuildForgedCallback(Guid orderId, bool succeeded)
    {
        var valid = BuildCallback(orderId, succeeded);
        return new Dictionary<string, string> { ["data"] = valid["data"], ["signature"] = "forged-signature-does-not-verify" };
    }

    private static string Sign(string data) =>
        Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(TestPrivateKey + data + TestPrivateKey)));
}

/// <summary>Captures outgoing messages so a test can read the code a phone would have received.</summary>
public sealed class CapturingSmsSender : ISmsSender
{
    private readonly List<(string PhoneNumber, string Message)> _sent = [];

    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        lock (_sent)
        {
            _sent.Add((phoneNumber, message));
        }

        return Task.CompletedTask;
    }

    public string LastCodeFor(string phoneNumber)
    {
        lock (_sent)
        {
            var message = _sent.Last(m => m.PhoneNumber == phoneNumber).Message;
            return new string(message.SkipWhile(c => !char.IsAsciiDigit(c)).TakeWhile(char.IsAsciiDigit).ToArray());
        }
    }
}

/// <summary>
/// Boots the real API pipeline — authentication, authorization, rate limiting, ProblemDetails —
/// against an in-memory database. Each instance is isolated, so limiter counters never leak
/// between tests.
/// </summary>
/// <summary>Lets a test move time forward without waiting for it.</summary>
public sealed class TestClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UtcNow;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>
/// Keeps uploaded objects in memory and stamps them with the test clock, so the reconciliation
/// sweep's age guard can be exercised. Also keeps the suite from writing real files into bin.
/// </summary>
public sealed class RecordingFileStorage(TestClock clock) : IFileStorage
{
    public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, DateTimeOffset> WrittenAt { get; } = new(StringComparer.Ordinal);

    public List<string> Deleted { get; } = [];

    public async Task<string> SaveAsync(Stream content, string key, string contentType, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        Objects[key] = buffer.ToArray();
        WrittenAt[key] = clock.UtcNow;

        return key;
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(Objects.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Deleted.Add(key);
        Objects.Remove(key);
        WrittenAt.Remove(key);

        return Task.CompletedTask;
    }

    public string GetPublicUrl(string key) => $"/uploads/{key}";

    public Task<IReadOnlyList<string>> ListKeysAsync(
        string prefix, DateTimeOffset modifiedBefore, CancellationToken cancellationToken = default)
    {
        var keys = Objects.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .Where(key => WrittenAt.GetValueOrDefault(key, DateTimeOffset.MinValue) < modifiedBefore)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(keys);
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"api-{Guid.NewGuid()}";
    private readonly IReadOnlyDictionary<string, string?>? _extraConfiguration;

    public CapturingSmsSender Sms { get; } = new();

    public TestClock Clock { get; } = new();

    public RecordingFileStorage Storage { get; }

    public RecordingPaymentGatewayClient Gateway { get; } = new();

    /// <param name="extraConfiguration">
    /// Layered on top of everything below, for the handful of tests about a setting's own default —
    /// the quota boundary tests need a small, specific limit that every other test must not inherit.
    /// </param>
    public ApiFactory(IReadOnlyDictionary<string, string?>? extraConfiguration = null)
    {
        Storage = new RecordingFileStorage(Clock);
        _extraConfiguration = extraConfiguration;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Jwt:SigningKey"] = "integration-test-signing-key-0000000000",
                ["Auth:Security:HashingKey"] = "integration-test-hashing-key-0000000000",
                ["Auth:Otp:ResendCooldown"] = "00:00:00",
                // Pinned to the production numbers. appsettings.Development.json raises them so the
                // browser tests are not throttled from a single address, and this host must not
                // inherit that: several tests here exist precisely to prove the limits bite.
                ["Auth:RateLimits:Otp:RequestPermitLimit"] = "5",
                ["Auth:RateLimits:Otp:VerifyPermitLimit"] = "10",
                ["Auth:RateLimits:Otp:Window"] = "00:15:00",
                ["Auth:RateLimits:Session:PermitLimit"] = "30",
                ["Auth:RateLimits:Session:Window"] = "00:01:00",
                ["Auth:RateLimits:ListingCreate:PermitLimit"] = "20",
                ["Auth:RateLimits:ListingCreate:Window"] = "00:10:00",
                ["Auth:RateLimits:MediaUpload:PermitLimit"] = "60",
                ["Auth:RateLimits:MediaUpload:Window"] = "00:10:00",
                ["Auth:RateLimits:PhoneReveal:PermitLimit"] = "20",
                ["Auth:RateLimits:PhoneReveal:Window"] = "01:00:00",
                ["Auth:RateLimits:StoreApply:PermitLimit"] = "5",
                ["Auth:RateLimits:StoreApply:Window"] = "1.00:00:00",
                ["Auth:RateLimits:ListingReport:PermitLimit"] = "10",
                ["Auth:RateLimits:ListingReport:Window"] = "00:10:00",
                ["Auth:RateLimits:PromotionOrderCreate:PermitLimit"] = "20",
                ["Auth:RateLimits:PromotionOrderCreate:Window"] = "00:10:00",
                ["Auth:RateLimits:PaymentCallback:PermitLimit"] = "120",
                ["Auth:RateLimits:PaymentCallback:Window"] = "00:01:00",
                ["Payments:FrontendBaseUrl"] = "https://ovcuprim.test",
                // B-1 defaults to 10 per category per month in code, same as production. Pinned
                // higher here for the same reason every rate limit above is pinned: this host tests
                // many unrelated behaviours by creating listings freely, and only the tests that ask
                // for a specific limit (via extraConfiguration) should ever see it bind.
                ["Listings:Quota:Default"] = "100000",
                ["ConnectionStrings:Default"] = "Host=unused;Database=unused;Username=unused;Password=unused"
            });

            if (_extraConfiguration is not null)
            {
                configuration.AddInMemoryCollection(_extraConfiguration);
            }
        });

        builder.ConfigureServices(services =>
        {
            // Swap PostgreSQL for the in-memory provider; everything else stays as production wires it.
            // EF 10 keeps the original UseNpgsql call as an IDbContextOptionsConfiguration<T>, so that
            // has to go as well or both providers end up configured on the same options object.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            services.RemoveAll<ISmsSender>();
            services.AddSingleton<ISmsSender>(Sms);

            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(Clock);

            services.RemoveAll<IFileStorage>();
            services.AddSingleton<IFileStorage>(Storage);

            services.RemoveAll<IPaymentGatewayClient>();
            services.AddSingleton<IPaymentGatewayClient>(Gateway);

            // The catalogue SQL is PostgreSQL-only (JSONB operators and the safe_numeric expression
            // indexes), so the in-memory host gets a store that applies the same visibility and
            // dimension filters through LINQ. It exercises the endpoint, the parser, the DTOs and
            // the authorization; the SQL itself is verified against a real database.
            services.RemoveAll<IListingSearchStore>();
            services.AddScoped<IListingSearchStore, InMemoryListingSearchStore>();
        });
    }

    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        // Follow nothing automatically; assertions are about status codes.
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost")
    });

    /// <summary>Promotes a registered user, so role-gated endpoints can be exercised.</summary>
    public async Task SetRoleAsync(string phoneNumber, UserRole role)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.SingleAsync(u => u.PhoneNumber == phoneNumber);
        user.Role = role;
        await db.SaveChangesAsync();
    }

    public async Task<User> GetUserAsync(string phoneNumber)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Users.SingleAsync(u => u.PhoneNumber == phoneNumber);
    }

    public async Task<List<string>> GetAuditActionsAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AuditLogs.Select(a => a.Action).ToListAsync();
    }

    /// <summary>
    /// Forces an order to Expired directly, so the "an expired order can never activate a promotion"
    /// rule can be tested without waiting for the maintenance sweep.
    /// </summary>
    public async Task ExpirePaymentOrderAsync(Guid paymentOrderId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = await db.PaymentOrders.SingleAsync(o => o.Id == paymentOrderId);
        order.Status = PaymentOrderStatus.Expired;
        await db.SaveChangesAsync();
    }

    /// <summary>Loads the real seed files so endpoint tests run against the production taxonomy.</summary>
    public async Task SeedTaxonomyAsync()
    {
        using var scope = Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<ITaxonomySeeder>();
        await seeder.SeedAsync(includeDevelopmentRegions: true);
    }
}

/// <summary>
/// A LINQ stand-in for the PostgreSQL search store. Deliberately covers only what the in-memory
/// provider can express — public visibility, category, region, store, seller type, price and sort —
/// because the point is to exercise the HTTP surface, not to re-implement the query.
/// </summary>
/// <remarks>
/// <b>Known coverage gap, tracked for pre-launch hardening.</b> Because this stands in for
/// <c>ListingSearchStore</c>, no test in either project exercises the hand-written SQL: not the
/// search predicate, not the facet reader, not <see cref="RefreshListingCountsAsync"/> (a no-op
/// here), and not the partial expression indexes the real statements are shaped around. Those are
/// verified by hand against PostgreSQL at the end of each phase. The fix is a PostgreSQL-backed
/// test project — Testcontainers or a CI service container — which adds a dependency and is
/// therefore deliberately deferred rather than smuggled in. Do not "improve" this class by
/// emulating PostgreSQL semantics: a stand-in that pretends to be the real thing turns an
/// acknowledged gap into a false green.
/// </remarks>
public sealed class InMemoryListingSearchStore(AppDbContext db) : IListingSearchStore
{
    public async Task<ListingIdPage> SearchAsync(ListingQuery query, CancellationToken cancellationToken = default)
    {
        var listings = await Filtered(query).ToListAsync(cancellationToken);

        var ordered = query.Sort switch
        {
            ListingSort.PriceAscending => listings.OrderBy(l => l.Price ?? decimal.MaxValue).ToList(),
            ListingSort.PriceDescending => listings.OrderByDescending(l => l.Price ?? decimal.MinValue).ToList(),
            _ => listings.OrderByDescending(l => l.BumpedAt).ToList()
        };

        var ids = ordered.Skip(query.Skip).Take(query.PageSize).Select(l => l.Id).ToList();

        return new ListingIdPage(ids, ordered.Count, true);
    }

    public async Task<ListingFacets> FacetsAsync(ListingQuery query, CancellationToken cancellationToken = default)
    {
        var all = await db.Listings.AsNoTracking()
            .Where(l => l.Status == ListingStatus.Active)
            .Select(l => new { l.CategoryId, l.RegionId })
            .ToListAsync(cancellationToken);

        return new ListingFacets(
            [.. all.GroupBy(l => l.CategoryId).Select(g => new FacetCount(g.Key, g.Count()))],
            [.. all.GroupBy(l => l.RegionId).Select(g => new FacetCount(g.Key, g.Count()))]);
    }

    public Task<int> RefreshListingCountsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public Task<int> ApplyViewCountsAsync(
        IReadOnlyDictionary<Guid, int> increments, CancellationToken cancellationToken = default) =>
        Task.FromResult(increments.Count);

    private IQueryable<Listing> Filtered(ListingQuery query)
    {
        // The one rule that must never drift from the real store: only live listings are public.
        var listings = db.Listings.AsNoTracking().Where(l => l.Status == ListingStatus.Active);

        if (query.CategoryIds.Count > 0)
        {
            listings = listings.Where(l => query.CategoryIds.Contains(l.CategoryId));
        }

        if (query.RegionId is { } regionId)
        {
            listings = listings.Where(l => l.RegionId == regionId);
        }

        if (query.StoreId is { } storeId)
        {
            listings = listings.Where(l => l.StoreId == storeId);
        }

        if (query.SellerType is { } sellerType)
        {
            listings = listings.Where(l => l.SellerType == sellerType);
        }

        if (query.PriceMin is { } min)
        {
            listings = listings.Where(l => l.Price >= min);
        }

        if (query.PriceMax is { } max)
        {
            listings = listings.Where(l => l.Price <= max);
        }

        if (query.Condition is { } condition)
        {
            listings = listings.Where(l => l.Condition == condition);
        }

        if (query.HasDelivery is { } delivery)
        {
            listings = listings.Where(l => l.HasDelivery == delivery);
        }

        if (query.HasText)
        {
            var text = query.Text!;
            listings = listings.Where(l => l.SearchKey != null && l.SearchKey.Contains(text));
        }

        return listings;
    }
}

file static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(T))
            {
                services.RemoveAt(i);
            }
        }
    }
}
