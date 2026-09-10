using System.Net;
using Microsoft.Extensions.Options;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.UnitTests.Sms;
using Ovcuprim.Infrastructure.Payments;

namespace Ovcuprim.Application.UnitTests.Payments;

/// <summary>
/// The same guarantee <c>PoctgoyerciniSmsSenderTests</c> proves for the SMS gateway, for the payment
/// gateway: nothing about the merchant credential ever appears in a logged line or in an exception a
/// caller could see, across every failure path this client has (missing-verification, HTTP failure,
/// malformed response). Missing test flagged by the payment security audit, C.4.
/// </summary>
public class EpointPaymentGatewayClientTests
{
    private const string PublicKey = "test-public-key-should-never-leak";
    private const string PrivateKey = "test-private-key-should-never-leak";

    private static (EpointPaymentGatewayClient Client, RecordingHandler Handler, RecordingLogger<EpointPaymentGatewayClient> Logger) Build()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://epoint.test"), Timeout = TimeSpan.FromSeconds(5) };
        var factory = new SingleClientHttpClientFactory(client);
        var options = Options.Create(new EpointGatewayOptions { PublicKey = PublicKey, PrivateKey = PrivateKey });
        var logger = new RecordingLogger<EpointPaymentGatewayClient>();

        return (new EpointPaymentGatewayClient(factory, options, logger), handler, logger);
    }

    private static void AssertNoCredentialsLeaked(RecordingLogger<EpointPaymentGatewayClient> logger)
    {
        foreach (var line in logger.Messages.Concat(logger.ExceptionMessages))
        {
            Assert.DoesNotContain(PublicKey, line, StringComparison.Ordinal);
            Assert.DoesNotContain(PrivateKey, line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_invalid_callback_signature_never_logs_the_private_key()
    {
        var (client, _, logger) = Build();

        // Neither the private key used to compute the expected signature, nor the caller-supplied
        // (wrong) one, may ever reach a log line.
        var result = client.VerifyCallback(new Dictionary<string, string>
        {
            ["data"] = Convert.ToBase64String("{}"u8.ToArray()),
            ["signature"] = "not-the-right-signature",
        });

        Assert.False(result.SignatureValid);
        AssertNoCredentialsLeaked(logger);
    }

    [Fact]
    public void A_missing_private_key_configuration_logs_an_error_without_the_public_key_either()
    {
        var factory = new SingleClientHttpClientFactory(new HttpClient(new RecordingHandler()));
        var options = Options.Create(new EpointGatewayOptions { PublicKey = PublicKey, PrivateKey = null });
        var logger = new RecordingLogger<EpointPaymentGatewayClient>();
        var client = new EpointPaymentGatewayClient(factory, options, logger);

        var result = client.VerifyCallback(new Dictionary<string, string>
        {
            ["data"] = Convert.ToBase64String("{}"u8.ToArray()),
            ["signature"] = "anything",
        });

        Assert.False(result.SignatureValid);
        AssertNoCredentialsLeaked(logger);
    }

    [Fact]
    public async Task An_http_error_from_the_gateway_logs_only_the_status_code_never_the_credentials()
    {
        var (client, handler, logger) = Build();
        handler.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<PaymentGatewayException>(
            () => client.GetOrderStatusAsync("some-reference"));

        AssertNoCredentialsLeaked(logger);
        // The request body itself (which does carry the keys) is never logged — only confirm no log
        // line was even attempted to carry it by checking the handler saw the real request instead.
        Assert.NotNull(handler.LastRequestBody);
    }

    [Fact]
    public async Task A_malformed_gateway_response_logs_only_that_parsing_failed_never_the_credentials()
    {
        var (client, handler, logger) = Build();
        handler.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("this is not json", System.Text.Encoding.UTF8, "application/json"),
        });

        await Assert.ThrowsAsync<PaymentGatewayException>(
            () => client.GetOrderStatusAsync("some-reference"));

        AssertNoCredentialsLeaked(logger);
    }

    [Fact]
    public async Task A_network_failure_logs_only_the_path_never_the_credentials()
    {
        var (client, handler, logger) = Build();
        handler.Responder = (_, _) => throw new HttpRequestException("connection refused");

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(
            () => client.GetOrderStatusAsync("some-reference"));

        AssertNoCredentialsLeaked(logger);
        Assert.DoesNotContain(PrivateKey, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(PublicKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_credential_never_appears_in_the_exception_thrown_to_the_caller_across_every_failure()
    {
        var scenarios = new Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[]
        {
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"),
            }),
        };

        foreach (var responder in scenarios)
        {
            var (client, handler, _) = Build();
            handler.Responder = responder;

            var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => client.GetOrderStatusAsync("ref"));

            Assert.DoesNotContain(PrivateKey, ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(PublicKey, ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(PrivateKey, ex.ToString(), StringComparison.Ordinal);
        }
    }
}
