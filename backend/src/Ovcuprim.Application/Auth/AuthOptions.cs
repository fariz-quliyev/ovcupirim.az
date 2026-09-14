namespace Ovcuprim.Application.Auth;

/// <summary>One-time-code policy. Every value is configurable so the rules can be tuned without a deploy.</summary>
public sealed class OtpOptions
{
    public const string SectionName = "Auth:Otp";

    /// <summary>Digits in a code. Six is the market norm for SMS.</summary>
    public int CodeLength { get; set; } = 6;

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>Wrong guesses allowed before the code is burned.</summary>
    public int MaxVerificationAttempts { get; set; } = 5;

    /// <summary>Minimum wait between two sends to the same number.</summary>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Per-number send ceiling inside <see cref="RequestWindow"/>, independent of the per-IP limiter.</summary>
    public int MaxRequestsPerWindow { get; set; } = 5;

    public TimeSpan RequestWindow { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>Access and refresh token policy.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Auth:Jwt";

    /// <summary>Shared by the startup validation and by the JwtBearer setup.</summary>
    public const int MinimumSigningKeyLength = 32;

    public string Issuer { get; set; } = "ovcupirim";

    public string Audience { get; set; } = "ovcupirim-web";

    /// <summary>HMAC signing key. Supplied by environment variable in production — never committed.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);
}

/// <summary>
/// Administrator password sign-in policy. The admin panel is reached this way and only this way —
/// an Admin account is excluded from the SMS flow entirely — so these values are the whole of what
/// stands between a guesser and the panel.
/// </summary>
public sealed class AdminLoginOptions
{
    public const string SectionName = "Auth:AdminLogin";

    /// <summary>
    /// The shortest password accepted. Long beats clever: this is a single account chosen by one
    /// person, not a rule a thousand users have to live with.
    /// </summary>
    public const int MinimumPasswordLength = 12;

    /// <summary>
    /// Bounds the work one request can ask for. PBKDF2 runs over an HMAC of the password, so the
    /// cost is already independent of length — this simply refuses the absurd.
    /// </summary>
    public const int MaximumPasswordLength = 256;

    /// <summary>Consecutive failures before the account locks.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// How long a locked account stays locked. Short on purpose: long enough to make guessing
    /// hopeless, short enough that an attacker cannot use it to keep the real administrator out.
    /// </summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>Server-side pepper for hashing OTP codes and refresh tokens before they touch the database.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Auth:Security";

    /// <summary>
    /// Shared by the startup validation and by <c>SecretHasher</c>, so the two can never disagree
    /// about what counts as configured.
    /// </summary>
    public const int MinimumHashingKeyLength = 32;

    /// <summary>The HMAC pepper. Validated at startup; see AddApplication.</summary>
    public string HashingKey { get; set; } = string.Empty;
}
