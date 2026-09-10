namespace Ovcuprim.Api.Infrastructure;

/// <summary>
/// The refresh token never reaches JavaScript: it lives in an HttpOnly cookie scoped to the auth
/// endpoints, so an XSS bug cannot read it and it is not sent with ordinary API calls.
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "ovcuprim_rt";

    private const string Path = "/api/v1/auth";

    public static void Write(HttpResponse response, string token, DateTimeOffset expiresAt)
    {
        response.Cookies.Append(Name, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = Path,
            Expires = expiresAt,
            IsEssential = true
        });
    }

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var token) && !string.IsNullOrWhiteSpace(token) ? token : null;

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = Path
        });
    }
}
