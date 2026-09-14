using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Auth;

public sealed record RegisterRequest(string PhoneNumber, string FullName);

public sealed record LoginRequest(string PhoneNumber);

public sealed record ResendOtpRequest(string PhoneNumber, OtpPurpose Purpose);

public sealed record VerifyOtpRequest(string PhoneNumber, string Code, OtpPurpose Purpose);

public sealed record UpdateProfileRequest(string FullName, string? Email);

/// <summary>Administrator sign-in. The phone number is the account's identity, not a channel — no SMS is sent.</summary>
public sealed record AdminLoginRequest(string PhoneNumber, string Password);

/// <summary>Changing one's own password. The current one is required, so a stolen session alone cannot lock the owner out.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// Deliberately uniform: the same body comes back whether or not the number belongs to an
/// account, so the endpoint cannot be used to enumerate registered users.
/// </summary>
public sealed record OtpRequestResponse(string Message, int ResendAfterSeconds, int CodeLength)
{
    public static OtpRequestResponse Default(int resendAfterSeconds, int codeLength) =>
        new("Təsdiq kodu göndərildi.", resendAfterSeconds, codeLength);
}

public sealed record UserDto(
    Guid Id,
    string PhoneNumber,
    string FullName,
    string? Email,
    string Role,
    bool IsPhoneVerified,
    DateTimeOffset CreatedAt);

/// <summary>What the API returns to the browser. The refresh token never appears here — it goes into an HttpOnly cookie.</summary>
public sealed record AuthResponse(string AccessToken, int ExpiresInSeconds, UserDto User);

/// <summary>Internal carrier: the response plus the raw refresh token the controller moves into the cookie.</summary>
public sealed record AuthTokens(AuthResponse Response, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
