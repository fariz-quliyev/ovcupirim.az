namespace Ovcuprim.Application.Abstractions;

/// <summary>Injectable clock so expiry and quota rules are testable.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
