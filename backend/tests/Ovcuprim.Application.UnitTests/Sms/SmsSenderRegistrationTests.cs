using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Infrastructure;
using Ovcuprim.Infrastructure.Sms;

namespace Ovcuprim.Application.UnitTests.Sms;

/// <summary>
/// What <c>AddInfrastructure</c> actually wires up for <see cref="ISmsSender"/>, exercised as DI
/// configuration rather than as HTTP behaviour — the unit tests in
/// <see cref="PoctgoyerciniSmsSenderTests"/> cover the sender itself.
/// </summary>
public class SmsSenderRegistrationTests
{
    private static ISmsSender Resolve(bool isDevelopment, Dictionary<string, string?> smsSettings)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=unused;Database=unused;Username=unused;Password=unused",
        };

        foreach (var (key, value) in smsSettings)
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        // AddInfrastructure assumes a host that already has logging registered — true of every real
        // entry point (Program.cs via Serilog, and the integration test host), just not of this bare
        // container built for wiring assertions alone.
        services.AddLogging();
        services.AddInfrastructure(configuration, isDevelopment);

        return services.BuildServiceProvider().GetRequiredService<ISmsSender>();
    }

    [Fact]
    public void Both_credentials_present_outside_Development_wires_up_the_gateway()
    {
        var sender = Resolve(isDevelopment: false, new Dictionary<string, string?>
        {
            ["Sms:Poctgoyercini:Username"] = "an-account",
            ["Sms:Poctgoyercini:Password"] = "a-password",
        });

        Assert.IsType<PoctgoyerciniSmsSender>(sender);
    }

    [Fact]
    public void Nothing_configured_outside_Development_keeps_the_safe_placeholder()
    {
        // The default remains "refuse to send" until an operator deliberately supplies credentials
        // for whichever account — Bumer's existing one or a separate one — they have decided to use.
        var sender = Resolve(isDevelopment: false, []);

        Assert.IsType<UnconfiguredSmsSender>(sender);
    }

    [Theory]
    [InlineData("only-username", null)]
    [InlineData(null, "only-password")]
    [InlineData("", "a-password")]
    [InlineData("an-account", "")]
    [InlineData("   ", "a-password")]
    public void A_half_configured_credential_pair_also_keeps_the_safe_placeholder(string? username, string? password)
    {
        var sender = Resolve(isDevelopment: false, new Dictionary<string, string?>
        {
            ["Sms:Poctgoyercini:Username"] = username,
            ["Sms:Poctgoyercini:Password"] = password,
        });

        Assert.IsType<UnconfiguredSmsSender>(sender);
    }

    [Fact]
    public void Development_ignores_gateway_configuration_entirely()
    {
        // Development's own end-to-end tests read codes back through IOtpProbe; wiring a real
        // gateway in underneath that, even if credentials happen to be present, would break them.
        var sender = Resolve(isDevelopment: true, new Dictionary<string, string?>
        {
            ["Sms:Poctgoyercini:Username"] = "an-account",
            ["Sms:Poctgoyercini:Password"] = "a-password",
        });

        Assert.IsType<DevelopmentSmsSender>(sender);
    }
}
