using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ovcuprim.Infrastructure.Persistence;

namespace Ovcuprim.Api.IntegrationTests;

/// <summary>
/// Secrets have to fail at startup, not on the first request that happens to need them.
/// </summary>
/// <remarks>
/// The JWT signing key already did. The peppering key did not: it threw from the
/// <c>SecretHasher</c> constructor, and because that service is resolved lazily a misconfigured
/// host started cleanly and then broke for the first real person trying to sign in. Both are now
/// validated at startup, and both are pinned here.
/// </remarks>
public class StartupConfigurationTests
{
    /// <summary>
    /// Boots the real host with one configuration value replaced. The database is swapped for the
    /// in-memory provider so the test is about configuration validation and nothing else.
    /// </summary>
    private sealed class ConfiguredFactory(Dictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Jwt:SigningKey"] = "integration-test-signing-key-0000000000",
                    ["Auth:Security:HashingKey"] = "integration-test-hashing-key-0000000000",
                    ["ConnectionStrings:Default"] = "Host=unused;Database=unused;Username=unused;Password=unused",
                });

                configuration.AddInMemoryCollection(settings);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<AppDbContext>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase($"startup-{Guid.NewGuid()}"));
            });
        }
    }

    private static Exception? StartupFailure(Dictionary<string, string?> settings)
    {
        try
        {
            using var factory = new ConfiguredFactory(settings);

            // Forces the host to build and start, which is when ValidateOnStart runs.
            _ = factory.Services.GetRequiredService<IConfiguration>();
            _ = factory.CreateClient();

            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public void A_complete_configuration_starts()
    {
        Assert.Null(StartupFailure([]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short")]
    [InlineData("0123456789012345678901234567890")]
    public void A_missing_or_short_hashing_key_stops_the_host_at_startup(string? key)
    {
        // 31 characters is the boundary: one short of the minimum.
        var failure = StartupFailure(new Dictionary<string, string?> { ["Auth:Security:HashingKey"] = key });

        Assert.NotNull(failure);
        Assert.Contains("HashingKey", Flatten(failure!), StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_of_exactly_the_minimum_length_is_accepted()
    {
        var failure = StartupFailure(new Dictionary<string, string?>
        {
            ["Auth:Security:HashingKey"] = new string('k', 32),
        });

        Assert.Null(failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short")]
    public void A_missing_or_short_signing_key_stops_the_host_at_startup(string? key)
    {
        var failure = StartupFailure(new Dictionary<string, string?> { ["Auth:Jwt:SigningKey"] = key });

        Assert.NotNull(failure);
        Assert.Contains("SigningKey", Flatten(failure!), StringComparison.Ordinal);
    }

    /*
     * The connection string is deliberately not covered here. It is read by AddInfrastructure from
     * builder.Configuration, which runs before this factory's ConfigureAppConfiguration callbacks
     * are applied — so the harness cannot make it absent, and a test that appeared to cover it
     * would only be asserting that the real appsettings value is present. The check itself is a
     * plain guard at registration time (blank counts as absent) and is verified by running the host
     * with the value cleared.
     *
     * The two keys below *are* covered, because ValidateOnStart resolves them from the built
     * configuration, which this harness can override.
     */

    /// <summary>The host wraps startup failures, so the message can be several levels down.</summary>
    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();

        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
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
