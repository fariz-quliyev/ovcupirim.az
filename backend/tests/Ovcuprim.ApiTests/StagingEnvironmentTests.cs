using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Infrastructure.Payments;
using Ovcuprim.Infrastructure.Persistence;
using Ovcuprim.Infrastructure.Sms;

namespace Ovcuprim.Api.IntegrationTests;

/// <summary>
/// Pins the one thing Staging is allowed to do differently from a real production host: use the
/// same simulated SMS/payment gateways Development does, because neither provider has a confirmed
/// sandbox yet (docs/payments-epoint.md, docs/sms-poctgoyercini.md). Everything else — host
/// filtering, storage validation, forwarded headers, HSTS — reads <c>IsDevelopment()</c> directly
/// in Program.cs and is untouched by this; <c>DeploymentHardeningTests</c> already proves those
/// reject Staging exactly like Production, since both are simply "not Development" to that code.
/// </summary>
public class StagingEnvironmentTests
{
    /// <summary>
    /// Boots the real host in Staging, with an in-memory database standing in for PostgreSQL.
    /// </summary>
    /// <remarks>
    /// Three checks — <c>AddInfrastructure</c>'s connection-string guard, <c>HostFilteringSetup</c>,
    /// and <c>StorageSetup</c> — all read their config value directly off <c>builder.Configuration</c>
    /// as part of <c>Program.cs</c>'s own top-level statements, which run before
    /// <see cref="ConfigureWebHost"/>'s configuration additions are woven in (the same limitation
    /// <c>StartupConfigurationTests</c> documents for the connection string specifically — it
    /// applies equally to these other two, which is why all three are set as real process
    /// environment variables here instead, the one thing the default configuration providers
    /// <c>WebApplication.CreateBuilder(args)</c> already include early enough to matter). Set for
    /// the shortest possible window and cleared on dispose; xUnit runs the methods in this one
    /// class sequentially by default, so nothing else in it can observe them.
    /// </remarks>
    private sealed class StagingFactory : WebApplicationFactory<Program>
    {
        private const string ConnectionStringVariable = "ConnectionStrings__Default";
        private const string AllowedHostsVariable = "AllowedHosts";
        private const string StorageRootVariable = "Storage__Local__RootPath";

        private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"ovcuprim-staging-test-{Guid.NewGuid():N}");

        public StagingFactory()
        {
            Directory.CreateDirectory(_storageRoot);
            Environment.SetEnvironmentVariable(
                ConnectionStringVariable, "Host=unused;Database=unused;Username=unused;Password=unused");
            // Must match the Host header the in-process test client actually sends (BaseAddress
            // below), or the framework's own host-filtering middleware correctly refuses it —
            // proving AllowedHosts is genuinely enforced in Staging is not this test's job (that
            // property is a plain function of IsDevelopment(), covered by DeploymentHardeningTests
            // and untouched by this whole change); this only needs requests to reach the app.
            Environment.SetEnvironmentVariable(AllowedHostsVariable, "localhost");
            Environment.SetEnvironmentVariable(StorageRootVariable, _storageRoot);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Staging);

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Jwt:SigningKey"] = "staging-test-signing-key-0000000000",
                    ["Auth:Security:HashingKey"] = "staging-test-hashing-key-00000000",
                    // Deliberately blank, exactly as a real staging deployment ships them: proves
                    // the simulated gateways are wired in on the environment alone, not because a
                    // real credential happens to be configured.
                    ["Sms:Poctgoyercini:Username"] = "",
                    ["Sms:Poctgoyercini:Password"] = "",
                    ["Payments:Epoint:PublicKey"] = "",
                    ["Payments:Epoint:PrivateKey"] = "",
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<AppDbContext>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase($"staging-{Guid.NewGuid()}"));
            });
        }

        public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Environment.SetEnvironmentVariable(ConnectionStringVariable, null);
            Environment.SetEnvironmentVariable(AllowedHostsVariable, null);
            Environment.SetEnvironmentVariable(StorageRootVariable, null);

            try
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of a throwaway temp directory; nothing depends on it being gone.
            }
        }
    }

    [Fact]
    public void Staging_wires_the_simulated_sms_sender_with_no_real_credentials_configured()
    {
        using var factory = new StagingFactory();

        Assert.IsType<DevelopmentSmsSender>(factory.Services.GetRequiredService<ISmsSender>());
        Assert.NotNull(factory.Services.GetService<IOtpProbe>());
    }

    [Fact]
    public void Staging_wires_the_simulated_payment_gateway_with_no_real_credentials_configured()
    {
        using var factory = new StagingFactory();

        Assert.IsType<DevelopmentPaymentGatewayClient>(factory.Services.GetRequiredService<IPaymentGatewayClient>());
        Assert.NotNull(factory.Services.GetService<IPaymentGatewaySimulator>());
    }

    [Fact]
    public async Task Staging_maps_the_otp_probe_endpoint_so_a_registration_can_be_completed_without_a_real_sms_account()
    {
        await using var factory = new StagingFactory();
        var client = factory.CreateApiClient();

        // A route that did not exist would 404 too, so this only proves the mapping by first
        // making the code exist, then reading it back — the same round trip the E2E suite relies
        // on in Development.
        var phone = "+994501234567";
        await client.PostAsJsonAsync("/api/v1/auth/register", new { phoneNumber = phone, fullName = "Staging Yoxlaması" });

        var probe = await client.GetAsync($"/api/v1/dev/otp/{Uri.EscapeDataString(phone)}");

        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
    }

    [Fact]
    public async Task Staging_maps_the_development_checkout_simulator_endpoint()
    {
        await using var factory = new StagingFactory();
        var client = factory.CreateApiClient();

        // Exists-and-answers is the property being pinned; a real flow is already covered end to
        // end by 09-promotions.spec.ts against a Development host and by the unit/API payment
        // suites — this only proves Staging maps the same route Development does.
        var response = await client.GetAsync($"/api/v1/dev/payments/{Guid.NewGuid()}/checkout");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
