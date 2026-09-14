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

    /// <summary>Signs an administrator in by password. No SMS is involved at any point.</summary>
    Task<Result<AuthTokens>> AdminPasswordLoginAsync(AdminLoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Changes the caller's own password and ends every existing session.</summary>
    Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    Task<Result<AuthTokens>> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);

    Task<Result> LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);
}

public sealed class AuthService(
    IAppDbContext db,
    IOtpService otpService,
    ITokenService tokenService,
    ISecretHasher hasher,
    IPasswordHasher passwords,
    ISecureTokenGenerator generator,
    IDateTimeProvider clock,
    IOptions<JwtOptions> jwtOptions,
    IOptions<OtpOptions> otpOptions,
    IOptions<AdminLoginOptions> adminLoginOptions,
    ILogger<AuthService> logger) : IAuthService
{
    /// <summary>Returned for every failed credential exchange — never says which part was wrong.</summary>
    private const string GenericAuthFailure = "Giriş mümkün olmadı. Məlumatları yoxlayıb yenidən cəhd edin.";

    private readonly JwtOptions _jwt = jwtOptions.Value;
    private readonly OtpOptions _otp = otpOptions.Value;
    private readonly AdminLoginOptions _adminLogin = adminLoginOptions.Value;

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
        else if (existing.DeletedAt is not null || existing.Status != UserStatus.Active || existing.Role == UserRole.Admin)
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

        // An administrator is deliberately outside the SMS flow: the panel is reached by password
        // and nothing else, so no code is ever issued for that number and no code can ever verify
        // it. The response stays identical to everyone else's — saying "this number does not use
        // SMS" here would hand an attacker the administrator's number.
        if (user is null || user.Status != UserStatus.Active || user.Role == UserRole.Admin)
        {
            // Same shape, same timing budget, no code sent: the endpoint cannot enumerate accounts.
            logger.LogInformation("Login OTP requested for unknown, inactive or administrator {Phone}", Common.PhoneNumber.Mask(phone));
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

        // The Admin check is defence in depth. No code is ever issued for an administrator, so
        // reaching here with a valid one should be impossible — but "should be impossible" is not a
        // thing to leave standing between an SMS interception and the admin panel.
        if (user is null || user.Status != UserStatus.Active || user.Role == UserRole.Admin)
        {
            return Result<AuthTokens>.Failure(ResultError.Unauthorized, GenericAuthFailure);
        }

        // Passing the SMS challenge is what proves ownership of the number.
        user.IsPhoneVerified = true;
        user.LastLoginAt = clock.UtcNow;

        var tokens = await IssueTokensAsync(user, ipAddress, cancellationToken);

        return Result<AuthTokens>.Success(tokens);
    }

    public async Task<Result<AuthTokens>> AdminPasswordLoginAsync(
        AdminLoginRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var phone = Common.PhoneNumber.Normalize(request.PhoneNumber);

        var user = phone is null
            ? null
            : await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken);

        // Every rejection below answers with GenericAuthFailure and nothing else. Which one fired —
        // no such number, not an administrator, suspended, no password set, wrong password, locked —
        // must be indistinguishable from outside, or this endpoint becomes a way to find out which
        // number owns the panel and then to confirm a guess at its password.
        if (user is null || user.Role != UserRole.Admin || user.Status != UserStatus.Active || user.PasswordHash is null)
        {
            // Spend the same CPU a real verification would, so the answer's timing says nothing
            // about whether the account exists.
            passwords.Verify(request.Password, passwords.DecoyHash);

            logger.LogWarning(
                "Admin password sign-in rejected for {Phone} from {Ip}",
                phone is null ? "(invalid)" : Common.PhoneNumber.Mask(phone), ipAddress);

            return Result<AuthTokens>.Failure(ResultError.Unauthorized, GenericAuthFailure);
        }

        if (user.LockedUntil is { } lockedUntil)
        {
            if (lockedUntil > now)
            {
                passwords.Verify(request.Password, passwords.DecoyHash);

                RecordAdminAuth(user, "AdminLoginRejectedLocked", ipAddress, now, new { lockedUntil });
                await db.SaveChangesAsync(cancellationToken);

                logger.LogWarning("Admin password sign-in refused: account locked until {LockedUntil}", lockedUntil);

                return Result<AuthTokens>.Failure(ResultError.Unauthorized, GenericAuthFailure);
            }

            // The lock has lapsed. Clearing the counter with it is what gives the real administrator
            // a clean slate rather than one attempt before the next lock.
            user.LockedUntil = null;
            user.FailedLoginAttempts = 0;
        }

        if (!passwords.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;

            var locked = user.FailedLoginAttempts >= _adminLogin.MaxFailedAttempts;

            if (locked)
            {
                user.LockedUntil = now + _adminLogin.LockoutDuration;
            }

            RecordAdminAuth(user, "AdminLoginFailed", ipAddress, now, new
            {
                attempts = user.FailedLoginAttempts,
                lockedUntil = user.LockedUntil
            });

            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                "Admin password sign-in failed from {Ip} — attempt {Attempts} of {Max}{Locked}",
                ipAddress, user.FailedLoginAttempts, _adminLogin.MaxFailedAttempts,
                locked ? ", account now locked" : string.Empty);

            return Result<AuthTokens>.Failure(ResultError.Unauthorized, GenericAuthFailure);
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;

        RecordAdminAuth(user, "AdminLoginSucceeded", ipAddress, now, payload: null);

        var tokens = await IssueTokensAsync(user, ipAddress, cancellationToken);

        logger.LogInformation("Admin {UserId} signed in by password from {Ip}", user.Id, ipAddress);

        return Result<AuthTokens>.Success(tokens);
    }

    public async Task<Result> ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        // The caller is already authenticated, so there is no enumeration to worry about here and
        // the messages can be specific enough to be useful.
        if (user is null || user.Role != UserRole.Admin || user.PasswordHash is null)
        {
            return Result.Failure(ResultError.Forbidden, "Parol yalnız administrator hesabı üçün təyin olunur.");
        }

        if (user.Status != UserStatus.Active)
        {
            return Result.Failure(ResultError.Forbidden, "Hesab aktiv deyil.");
        }

        if (!passwords.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Result.Failure(ResultError.Validation, "Cari parol yanlışdır.");
        }

        var now = clock.UtcNow;

        user.PasswordHash = passwords.Hash(request.NewPassword);
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;

        // Every session dies with the old password, this one included. If the reason for the change
        // is that the password leaked, a change that leaves the thief's session alive is no change
        // at all — so the administrator signs in again afterwards, deliberately.
        await RevokeAllForUserAsync(user.Id, now, cancellationToken);

        RecordAdminAuth(user, "AdminPasswordChanged", ipAddress, now, payload: null);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Admin {UserId} changed their password; all sessions revoked", user.Id);

        return Result.Success();
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

    /// <summary>
    /// Queues an audit row for an administrator sign-in event. The caller saves — several of these
    /// sit alongside a user mutation that has to land in the same transaction.
    /// </summary>
    /// <remarks>
    /// The password never appears in the payload, correct or otherwise, and neither does any part
    /// of it. What is recorded is the attempt, its address and the resulting lock state.
    /// </remarks>
    private void RecordAdminAuth(User user, string action, string? ipAddress, DateTimeOffset now, object? payload)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = user.Id,
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            Action = action,
            PayloadJson = payload is null ? null : AuditPayload.From(payload),
            IpAddress = ipAddress,
            CreatedAt = now
        });
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
