using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Infrastructure.Sms;

namespace Ovcuprim.Application.UnitTests.Sms;

/// <summary>
/// A transport double: returns whatever the test configures, records the outgoing request (so its
/// JSON shape can be inspected), and lets a test simulate a network failure, a gateway timeout, or
/// caller cancellation without either a real network call or a real clock-driven timeout.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastRequestBody { get; private set; }

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        return await Responder(request, cancellationToken);
    }
}

/// <summary>Always hands back the one client under test, the way DI's named-client lookup would.</summary>
internal sealed class SingleClientHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

/// <summary>
/// Captures every formatted log line and every exception logged with it, so a test can assert a
/// secret or an OTP value never appears in any of them — the point of the assertion is the log
/// output actually produced, not the source code's intentions.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public List<string> ExceptionMessages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));

        if (exception is not null)
        {
            ExceptionMessages.Add(exception.Message);
        }
    }
}

public class PoctgoyerciniSmsSenderTests
{
    private const string Username = "ovcupirim_test_user";
    private const string Password = "correct-horse-battery-staple";
    private const string SecretOtp = "482913";

    private static (PoctgoyerciniSmsSender Sender, RecordingHandler Handler, RecordingLogger<PoctgoyerciniSmsSender> Logger) Build()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var factory = new SingleClientHttpClientFactory(client);
        var options = Options.Create(new PoctgoyerciniSmsOptions { Username = Username, Password = Password });
        var logger = new RecordingLogger<PoctgoyerciniSmsSender>();

        return (new PoctgoyerciniSmsSender(factory, options, logger), handler, logger);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) => new(status)
    {
        Content = JsonContent.Create(body)
    };

    [Fact]
    public async Task A_successful_send_completes_without_throwing()
    {
        var (sender, handler, _) = Build();
        handler.Responder = (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, new { StatusCode = 200 }));

        await sender.SendAsync("+994501234567", $"Ovcupirim.az təsdiq kodu: {SecretOtp}.");

        Assert.NotNull(handler.LastRequest);
    }

    [Fact]
    public async Task The_request_body_carries_exactly_the_gateways_expected_shape()
    {
        var (sender, handler, _) = Build();
        handler.Responder = (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, new { StatusCode = 200 }));

        await sender.SendAsync("0501234567", "the message");

        Assert.NotNull(handler.LastRequestBody);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var root = body.RootElement;

        // Exactly these four fields, in the exact casing the gateway documents — a camelCase
        // fallback from a stray ambient serializer option would silently break delivery.
        Assert.Equal(Username, root.GetProperty("Username").GetString());
        Assert.Equal(Password, root.GetProperty("Password").GetString());
        Assert.Equal("the message", root.GetProperty("Message").GetString());

        var receivers = root.GetProperty("Receivers");
        Assert.Equal(JsonValueKind.Array, receivers.ValueKind);
        Assert.Single(receivers.EnumerateArray());

        // OvcuPirim's own normaliser ran ("0501234567" -> E.164), and the leading '+' was stripped
        // for the wire format — matching what Bumer.az's own integration sends.
        Assert.Equal("994501234567", receivers[0].GetString());

        Assert.Equal(4, CountProperties(root));

        static int CountProperties(JsonElement element)
        {
            var count = 0;
            foreach (var _ in element.EnumerateObject())
            {
                count++;
            }

            return count;
        }
    }

    [Fact]
    public async Task Message_content_reaches_the_gateway_unchanged()
    {
        // OvcuPirim composes its own template (OtpService.cs) — the sender must not alter, wrap or
        // re-template it.
        var (sender, handler, _) = Build();
        handler.Responder = (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, new { StatusCode = 200 }));

        const string exact = "Ovcupirim.az təsdiq kodu: 482913. Kod 5 dəqiqə etibarlıdır. Kodu heç kimlə paylaşmayın.";
        await sender.SendAsync("+994501234567", exact);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(exact, body.RootElement.GetProperty("Message").GetString());
    }

    [Fact]
    public async Task A_non_200_provider_status_code_throws_a_delivery_exception()
    {
        var (sender, handler, _) = Build();
        handler.Responder = (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, new { StatusCode = 400 }));

        var ex = await Assert.ThrowsAsync<SmsDeliveryException>(
            () => sender.SendAsync("+994501234567", "message"));

        Assert.Contains("400", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_status_code_is_treated_as_failure_not_success()
    {
        var (sender, handler, _) = Build();
        handler.Responder = (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, new { SomethingElse = "ok" }));

        await Assert.ThrowsAsync<SmsDeliveryException>(() => sender.SendAsync("+994501234567", "message"));
    }

    [Fact]
    public async Task A_malformed_response_body_throws_a_delivery_exception_not_a_raw_json_exception()
    {
        var (sender, handler, _) = Build();
        handler.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("this is not json", System.Text.Encoding.UTF8, "application/json")
        });

        await Assert.ThrowsAsync<SmsDeliveryException>(() => sender.SendAsync("+994501234567", "message"));
    }

    [Fact]
    public async Task An_http_error_status_throws_a_delivery_exception()
    {
        var (sender, handler, _) = Build();
        handler.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsAsync<SmsDeliveryException>(
            () => sender.SendAsync("+994501234567", "message"));

        Assert.Contains("500", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_network_failure_throws_a_delivery_exception()
    {
        var (sender, handler, _) = Build();
        handler.Responder = (_, _) => throw new HttpRequestException("connection refused");

        await Assert.ThrowsAsync<SmsDeliveryException>(() => sender.SendAsync("+994501234567", "message"));
    }

    [Fact]
    public async Task A_gateway_timeout_throws_a_delivery_exception_not_a_raw_cancellation()
    {
        // Simulates HttpClient's own request timeout: the handler raises TaskCanceledException
        // while the CALLER's token was never touched, which is exactly what a real Timeout expiry
        // looks like from the sender's point of view.
        var (sender, handler, _) = Build();
        handler.Responder = (_, _) => throw new TaskCanceledException("the request timed out");

        var ex = await Assert.ThrowsAsync<SmsDeliveryException>(
            () => sender.SendAsync("+994501234567", "message"));

        Assert.IsType<TaskCanceledException>(ex.InnerException);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_as_itself_not_as_a_delivery_exception()
    {
        var (sender, handler, _) = Build();
        handler.Responder = (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, new { StatusCode = 200 }));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => sender.SendAsync("+994501234567", "message", cts.Token));
    }

    [Fact]
    public async Task An_unrecognisable_phone_number_is_refused_before_any_network_call()
    {
        var (sender, handler, _) = Build();
        var called = false;
        handler.Responder = (_, _) =>
        {
            called = true;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, new { StatusCode = 200 }));
        };

        // Not a made-up validation path — OvcuPirim's own PhoneNumber.Normalize is what decides this,
        // the same rule every other phone number in the application is held to.
        await Assert.ThrowsAsync<SmsDeliveryException>(() => sender.SendAsync("not a phone number", "message"));

        Assert.False(called, "the gateway must never be called for a number OvcuPirim itself would reject");
    }

    [Fact]
    public async Task The_credential_and_the_message_never_appear_in_a_logged_line_on_any_path()
    {
        var scenarios = new (string Label, HttpResponseMessage Response)[]
        {
            ("success", JsonResponse(HttpStatusCode.OK, new { StatusCode = 200 })),
            ("provider-rejected", JsonResponse(HttpStatusCode.OK, new { StatusCode = 400 })),
            ("http-error", new HttpResponseMessage(HttpStatusCode.InternalServerError)),
        };

        foreach (var (label, response) in scenarios)
        {
            var (sender, handler, logger) = Build();
            handler.Responder = (_, _) => Task.FromResult(response);

            try
            {
                await sender.SendAsync("+994501234567", $"code is {SecretOtp}");
            }
            catch (SmsDeliveryException)
            {
                // Expected for the failure scenarios; the assertion below is what this test is about.
            }

            foreach (var line in logger.Messages.Concat(logger.ExceptionMessages))
            {
                Assert.DoesNotContain(Password, line, StringComparison.Ordinal);
                Assert.DoesNotContain(Username, line, StringComparison.Ordinal);
                Assert.DoesNotContain(SecretOtp, line, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task The_credential_never_appears_in_the_exception_thrown_to_the_caller()
    {
        var (sender, handler, _) = Build();
        handler.Responder = (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, new { StatusCode = 400 }));

        var ex = await Assert.ThrowsAsync<SmsDeliveryException>(
            () => sender.SendAsync("+994501234567", $"code is {SecretOtp}"));

        Assert.DoesNotContain(Password, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Username, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretOtp, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, ex.ToString(), StringComparison.Ordinal);
    }
}
