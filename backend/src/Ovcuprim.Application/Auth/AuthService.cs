using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Auth;

public interface IAuthService
{
    Task<Result<OtpRequestResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<Result<OtpRequestResponse>> RequestLoginOtpAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<Result<OtpRequestResponse>> ResendOtpAsync(ResendOtpRequest request, CancellationToken cancellationToken = default);

    Task<Result<AuthTokens>> VerifyOtpAsync(VerifyOtpRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    Task<Result<AuthTokens>> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);

    Task<Result> LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);
}

public sealed class AuthService(
    IAppDbContext db,
    IOtpService otpService,
    ITokenService tokenService,
    ISecretHasher hasher,
    ISecureTokenGenerator generator,
    IDateTimeProvider clock,
    IOptions<JwtOptions> jwtOptions,
    IOptions<OtpOptions> otpOptions,
    ILogger<AuthService> logger) : IAuthService
{
    /// <summary>Returned for every failed credential exchange — never says which part was wrong.</summary>
    private const string GenericAuthFailure = "Giriş mümkün olmadı. Məlumatları yoxlayıb yenidən cəhd edin.";

    private readonly JwtOptions _jwt = jwtOptions.Value;
    private readonly OtpOptions _otp = otpOptions.Value;

    public async Task<Result<OtpRequestResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var phone = Common.PhoneNumber.Normalize(request.PhoneNumber);
        if (phone is null)
        {
            return Result<OtpRequestResponse>.Failure(ResultError.Validation, "Telefon nömrəsi düzgün deyil.");
        }

        // Soft-deleted accounts still hold the unique phone number, so look past the query filter.
        var existing = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken);

        if (existing is null)
        {
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = phone,
                FullName = request.FullName.Trim(),
                Role = UserRole.User,
                Status = UserStatus.Active,
                IsPhoneVerified = false,
                CreatedAt = clock.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
        }
        else if (existing.DeletedAt is not null || existing.Status != UserStatus.Active)
        {
            // Nothing is sent, but the caller gets the same answer as everyone else.
            logger.LogInformation("Registration attempt on unavailable account {Phone}", Common.PhoneNumber.Mask(phone));
            return Result<OtpRequestResponse>.Success(DefaultOtpResponse());
        }

        // An existing active account simply receives a code and signs in — no "already registered" oracle.
        var issue = await otpService.IssueAsync(phone, OtpPurpose.Registration, cancellationToken);

        return issue.Succeeded
            ? Result<OtpRequestResponse>.Success(DefaultOtpResponse())
            : Result<OtpRequestResponse>.Failure(issue.Error, issue.Message ?? "Kod göndərilmədi.");
    }

    public async Task<Result<OtpRequestResponse>> RequestLoginOtpAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var phone = Common.PhoneNumber.Normalize(request.PhoneNumber);
        if (phone is null)
        {
            return Result<OtpRequestResponse>.Failure(ResultError.Validation, "Telefon nömrəsi düzgün deyil.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken);

        if (user is null || user.Status != UserStatus.Active)
        {
            // Same shape, same timing budget, no code sent: the endpoint cannot enumerate accounts.
            logger.LogInformation("Login OTP requested for unknown or inactive {Phone}", Common.PhoneNumber.Mask(phone));
            return Result<OtpRequestResponse>.Success(DefaultOtpResponse());
        }

        var issue = await otpService.IssueAsync(phone, OtpPurpose.Login, cancellationToken);

        return issue.Succeeded
            ? Result<OtpRequestResponse>.Success(DefaultOtpResponse())
            : Result<OtpRequestResponse>.Failure(issue.Error, issue.Message ?? "Kod göndərilmədi.");
    }

    public Task<Result<OtpRequestResponse>> ResendOtpAsync(ResendOtpRequest request, CancellationToken cancellationToken = default) =>
        request.Purpose == OtpPurpose.Registration
            ? RegisterResendAsync(request, cancellationToken)
            : RequestLoginOtpAsync(new LoginRequest(request.PhoneNumber), cancellationToken);

    public async Task<Result<AuthTokens>> VerifyOtpAsync(VerifyOtpRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var phone = Common.PhoneNumber.Normalize(request.PhoneNumber);
        if (phone is null)
        {
            return Result<AuthTokens>.Failure(ResultError.Validation, "Telefon nömrəsi düzgün deyil.");
        }

        var verification = await otpService.VerifyAsync(phone, request.Code, request.Purpose, cancellationToken);
        if (!verification.Succeeded)
        {
            return Result<AuthTokens>.Failure(verification.Error, verification.Message ?? GenericAuthFailure);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken);

        if (user is null || user.Status != UserStatus.Active)
        {
            return Result<AuthTokens>.Failure(ResultError.Unauthorized, GenericAuthFailure);
        }

        // Passing the SMS challenge is what proves ownership of the number.
        user.IsPhoneVerified = true;
        user.LastLoginAt = clock.UtcNow;

        var tokens = await IssueTokensAsync(user, ipAddress, cancellationToken);

        return Result<AuthTokens>.Success(tokens);
    }

    public async Task<Result<AuthTokens>> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Result<AuthTokens>.Failure(ResultError.Unauthorized, GenericAuthFailure);
        }

        var hash = hasher.Hash(refreshToken);

        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null)
        {
            return Result<AuthTokens>.Failure(ResultError.Unauthorized, GenericAuthFailure);
        }

        var now = clock.UtcNow;

        // Replay: a rotated token is presented a second time. Treat the whole session as compromised.
        if (stored.RevokedAt is not null)
        {
            await RevokeAllForUserAsync(stored.UserId, now, cancellationToken);

            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = stored.UserId,
                EntityType = nameof(RefreshToken),
                EntityId = stored.Id.ToString(),
                Action = "RefreshTokenReuseDetected",
                PayloadJson = AuditPayload.From(new { revokedAt = stored.RevokedAt, presentedFrom = ipAddress }),
                IpAddress = ipAddress,
                CreatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning("Refresh token reuse detected for user {UserId}; all sessions revoked", stored.UserId);

            return Result<AuthTokens>.Failure(ResultError.Unauthorized, GenericAuthFailure);
        }

        if (stored.ExpiresAt <= now)
        {
            return Result<AuthTokens>.Failure(ResultError.Unauthorized, GenericAuthFailure);
        }

        if (stored.User.Status != UserStatus.Active)
        {
            await RevokeAllForUserAsync(stored.UserId, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Result<AuthTokens>.Failure(ResultError.Unauthorized, GenericAuthFailure);
        }

        var tokens = await IssueTokensAsync(stored.User, ipAddress, cancellationToken, rotating: stored);

        return Result<AuthTokens>.Success(tokens);
    }

    public async Task<Result> LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        // Logging out is always "successful" — an invalid cookie is not an error worth reporting.
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Result.Success();
        }

        var hash = hasher.Hash(refreshToken);

        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is not null && stored.RevokedAt is null)
        {
            stored.RevokedAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    private async Task<Result<OtpRequestResponse>> RegisterResendAsync(ResendOtpRequest request, CancellationToken cancellationToken)
    {
        var phone = Common.PhoneNumber.Normalize(request.PhoneNumber);
        if (phone is null)
        {
            return Result<OtpRequestResponse>.Failure(ResultError.Validation, "Telefon nömrəsi düzgün deyil.");
        }

        var issue = await otpService.IssueAsync(phone, OtpPurpose.Registration, cancellationToken);

        return issue.Succeeded
            ? Result<OtpRequestResponse>.Success(DefaultOtpResponse())
            : Result<OtpRequestResponse>.Failure(issue.Error, issue.Message ?? "Kod göndərilmədi.");
    }

    private async Task<AuthTokens> IssueTokensAsync(
        User user,
        string? ipAddress,
        CancellationToken cancellationToken,
        RefreshToken? rotating = null)
    {
        var now = clock.UtcNow;
        var access = tokenService.CreateAccessToken(user);

        var raw = generator.GenerateRefreshToken();
        var refresh = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = hasher.Hash(raw),
            ExpiresAt = now + _jwt.RefreshTokenLifetime,
            CreatedByIp = ipAddress,
            CreatedAt = now
        };

        db.RefreshTokens.Add(refresh);

        // Rotation: the presented token dies the moment its successor is born.
        if (rotating is not null)
        {
            rotating.RevokedAt = now;
            rotating.ReplacedByTokenId = refresh.Id;
        }

        await db.SaveChangesAsync(cancellationToken);

        var response = new AuthResponse(
            access.Value,
            (int)(access.ExpiresAt - now).TotalSeconds,
            ToDto(user));

        return new AuthTokens(response, raw, refresh.ExpiresAt);
    }

    private async Task RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in active)
        {
            token.RevokedAt = now;
        }
    }

    private OtpRequestResponse DefaultOtpResponse() =>
        OtpRequestResponse.Default((int)_otp.ResendCooldown.TotalSeconds, _otp.CodeLength);

    internal static UserDto ToDto(User user) => new(
        user.Id,
        user.PhoneNumber,
        user.FullName,
        user.Email,
        user.Role.ToString(),
        user.IsPhoneVerified,
        user.CreatedAt);
}
