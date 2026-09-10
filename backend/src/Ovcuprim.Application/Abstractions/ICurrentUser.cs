namespace Ovcuprim.Application.Abstractions;

/// <summary>The authenticated caller, resolved from the access token.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    string? Role { get; }

    bool IsAuthenticated { get; }

    bool IsInRole(string role);
}
