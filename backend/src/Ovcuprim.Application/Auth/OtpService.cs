using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Auth;

public interface IOtpService
{
    /// <summary>Issues and sends a code, enforcing the resend cooldown and the per-number window cap.</summary>
    Task<Result> IssueAsync(string phoneNumber, OtpPurpose purpose, CancellationToken cancellationToken = default);

    /// <summary>Consumes the newest live code. Failure messages are deliberately identical.</summary>
    Task<Result> VerifyAsync(string phoneNumber, string code, OtpPurpose purpose, CancellationToken cancellationToken = default);
}

public sealed class OtpService(
    IAppDbContext db,
    ISecretHasher hasher,
    ISecureTokenGenerator generator,
    ISmsSender smsSender,
    IDateTimeProvider clock,
    IOptions<OtpOptions> options,
    ILogger<OtpService> logger) : IOtpService
{
    /// <summary>The single message every verification failure returns, so nothing can be inferred from it.</summary>
    private const string GenericVerifyFailure = "Kod yanlışdır və ya vaxtı bitib.";

    private readonly OtpOptions _options = options.Value;

    public async Task<Result> IssueAsync(string phoneNumber, OtpPurpose purpose, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        var recent = await db.OtpCodes
            .Where(o => o.PhoneNumber == phoneNumber && o.CreatedAt > now - _options.RequestWindow)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        // Cooldown: one send per number per cooldown period, whatever the purpose.
        var last = recent.FirstOrDefault();
        if (last is not null)
        {
            var readyAt = last.CreatedAt + _options.ResendCooldown;
            if (readyAt > now)
            {
                var seconds = (int)Math.Ceiling((readyAt - now).TotalSeconds);
                return Result.Failure(ResultError.RateLimited, $"Yeni kod {seconds} saniyə sonra istənilə bilər.");
            }
        }

        // Window cap: blunts SMS-pumping against a single number even from many IPs.
        if (recent.Count >= _options.MaxRequestsPerWindow)
        {
            logger.LogWarning("OTP window cap reached for {Phone}", PhoneNumber.Mask(phoneNumber));
            return Result.Failure(ResultError.RateLimited, "Kod sorğusu limiti aşıldı. Bir müddət sonra yenidən cəhd edin.");
        }

        // Only the newest code may ever be valid.
        var live = await db.OtpCodes
            .Where(o => o.PhoneNumber == phoneNumber && o.Purpose == purpose && o.ConsumedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var stale in live)
        {
            stale.ConsumedAt = now;
        }

        var code = generator.GenerateNumericCode(_options.CodeLength);

        db.OtpCodes.Add(new OtpCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber,
            CodeHash = hasher.Hash(code),
            Purpose = purpose,
            ExpiresAt = now + _options.Lifetime,
            AttemptCount = 0,
            CreatedAt = now
        });

        await db.SaveChangesAsync(cancellationToken);

        var minutes = (int)_options.Lifetime.TotalMinutes;
        await smsSender.SendAsync(
            phoneNumber,
            $"Ovcupirim.az təsdiq kodu: {code}. Kod {minutes} dəqiqə etibarlıdır. Kodu heç kimlə paylaşmayın.",
            cancellationToken);

        return Result.Success();
    }

    public async Task<Result> VerifyAsync(string phoneNumber, string code, OtpPurpose purpose, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        var otp = await db.OtpCodes
            .Where(o => o.PhoneNumber == phoneNumber && o.Purpose == purpose && o.ConsumedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (otp is null)
        {
            return Result.Failure(ResultError.Validation, GenericVerifyFailure);
        }

        if (otp.ExpiresAt <= now)
        {
            otp.ConsumedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure(ResultError.Validation, GenericVerifyFailure);
        }

        if (otp.AttemptCount >= _options.MaxVerificationAttempts)
        {
            otp.ConsumedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure(ResultError.RateLimited, "Cəhd limiti aşıldı. Yeni kod istəyin.");
        }

        if (!hasher.Verify(code, otp.CodeHash))
        {
            otp.AttemptCount++;

            // Burn the code on the last allowed miss rather than leaving it guessable.
            if (otp.AttemptCount >= _options.MaxVerificationAttempts)
            {
                otp.ConsumedAt = now;
                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning("OTP attempt limit reached for {Phone}", PhoneNumber.Mask(phoneNumber));
                return Result.Failure(ResultError.RateLimited, "Cəhd limiti aşıldı. Yeni kod istəyin.");
            }

            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure(ResultError.Validation, GenericVerifyFailure);
        }

        otp.ConsumedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
