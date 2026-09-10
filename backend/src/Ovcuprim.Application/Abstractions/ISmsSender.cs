namespace Ovcuprim.Application.Abstractions;

/// <summary>Sends one-time codes. The development implementation writes to the log instead of billing an SMS.</summary>
public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default);
}

/// <summary>
/// A message did not reach the gateway, or the gateway did not accept it. Every <see cref="ISmsSender"/>
/// implementation that can fail throws this rather than a provider-specific exception type, so a
/// caller (or a test) can handle "the code was not sent" uniformly regardless of which gateway is
/// wired in. The message is written for an operator reading a log, not for the end user — it never
/// contains a credential, a one-time code, or the request/response body.
/// </summary>
public sealed class SmsDeliveryException : Exception
{
    public SmsDeliveryException(string message) : base(message)
    {
    }

    public SmsDeliveryException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
