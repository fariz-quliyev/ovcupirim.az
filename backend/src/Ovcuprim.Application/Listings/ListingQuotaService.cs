using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Application.Listings;

/// <summary>
/// How many listings a user may publish free of charge in one category per period. The number is
/// policy, not code: until it is configured every category is unlimited, so the mechanism can ship
/// and be tested before the product decision lands.
/// </summary>
public interface IListingQuotaPolicy
{
    /// <summary>Null means no limit for this category.</summary>
    int? LimitFor(int categoryId);
}

public interface IListingQuotaService
{
    /// <summary>Checks without consuming, for the pre-flight the form does.</summary>
    Task<Result> CheckAsync(Guid userId, int categoryId, CancellationToken cancellationToken = default);

    /// <summary>Checks and, when allowed, records one use against the current period.</summary>
    Task<Result> TryConsumeAsync(Guid userId, int categoryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ListingLimitDto>> GetUsageAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads <c>Listings:Quota:Default</c> and <c>Listings:Quota:Categories:{categoryId}</c>.
/// </summary>
/// <remarks>
/// B-1: 10 free submissions per category per calendar month is the approved product number, and it
/// is the default an unconfigured host gets — not "unlimited". Unlimited is now something a
/// non-production host has to ask for explicitly, by configuring a number large enough that the
/// limit is never reached; that is what Development and the test hosts do, for the same reason they
/// loosen the OTP and session rate limits. <see cref="DefaultLimit"/> stays a compiled-in constant
/// rather than living only in appsettings.json, matching how every other approved numeric policy on
/// this API (the rate-limit windows, the OTP attempt counts) is expressed: the number a production
/// deployment gets without touching configuration is the number in the code, and configuration only
/// ever overrides it.
/// </remarks>
public sealed class ConfigurationListingQuotaPolicy(IConfiguration configuration) : IListingQuotaPolicy
{
    public const string SectionName = "Listings:Quota";

    /// <summary>B-1 — 10 free listing submissions per category per calendar month.</summary>
    public const int DefaultLimit = 10;

    public int? LimitFor(int categoryId)
    {
        var specific = configuration[$"{SectionName}:Categories:{categoryId}"];

        if (int.TryParse(specific, out var categoryLimit))
        {
            return categoryLimit;
        }

        return int.TryParse(configuration[$"{SectionName}:Default"], out var fallback) ? fallback : DefaultLimit;
    }
}

public sealed class ListingQuotaService(
    IAppDbContext db,
    IListingQuotaPolicy policy,
    IDateTimeProvider clock) : IListingQuotaService
{
    public async Task<Result> CheckAsync(Guid userId, int categoryId, CancellationToken cancellationToken = default)
    {
        var limit = policy.LimitFor(categoryId);

        if (limit is null)
        {
            return Result.Success();
        }

        var used = await UsedAsync(userId, categoryId, PeriodStart(), cancellationToken);

        return used < limit
            ? Result.Success()
            : Result.Failure(ResultError.Conflict, LimitReachedMessage(limit.Value));
    }

    public async Task<Result> TryConsumeAsync(Guid userId, int categoryId, CancellationToken cancellationToken = default)
    {
        var limit = policy.LimitFor(categoryId);

        if (limit is null)
        {
            return Result.Success();
        }

        var period = PeriodStart();
        var quota = await db.ListingQuotas
            .FirstOrDefaultAsync(q => q.UserId == userId && q.CategoryId == categoryId && q.PeriodStart == period, cancellationToken);

        if (quota is null)
        {
            quota = new ListingQuota
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                CategoryId = categoryId,
                PeriodStart = period,
                UsedCount = 0
            };

            db.ListingQuotas.Add(quota);
        }

        if (quota.UsedCount >= limit)
        {
            return Result.Failure(ResultError.Conflict, LimitReachedMessage(limit.Value));
        }

        quota.UsedCount++;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two publishes raced for the same (user, category, period). The unique index caught it;
            // re-read and let the caller retry rather than double-counting.
            return Result.Failure(ResultError.Conflict, "Elan limiti yoxlanılarkən münaqişə baş verdi. Yenidən cəhd edin.");
        }

        return Result.Success();
    }

    public async Task<IReadOnlyList<ListingLimitDto>> GetUsageAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var period = PeriodStart();
        // Midnight on the 1st in Baku, not in UTC — the instant the period this method just
        // computed in local time actually turns over.
        var nextPeriod = new DateTimeOffset(period.AddMonths(1).ToDateTime(TimeOnly.MinValue), AzerbaijanOffset);

        var rows = await db.ListingQuotas
            .AsNoTracking()
            .Where(q => q.UserId == userId && q.PeriodStart == period)
            .Select(q => new { q.CategoryId, q.Category.Slug, q.Category.NameAz, q.UsedCount })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r =>
            {
                var limit = policy.LimitFor(r.CategoryId);
                var exhausted = limit is not null && r.UsedCount >= limit;

                return new ListingLimitDto(
                    r.CategoryId,
                    r.Slug,
                    r.NameAz,
                    r.UsedCount,
                    limit,
                    exhausted ? nextPeriod : null);
            })
            .OrderBy(r => r.CategoryNameAz, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<int> UsedAsync(Guid userId, int categoryId, DateOnly period, CancellationToken cancellationToken) =>
        await db.ListingQuotas
            .AsNoTracking()
            .Where(q => q.UserId == userId && q.CategoryId == categoryId && q.PeriodStart == period)
            .Select(q => q.UsedCount)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Azerbaijan has used a fixed UTC+4 offset year-round since abolishing DST in 2016.</summary>
    private static readonly TimeSpan AzerbaijanOffset = TimeSpan.FromHours(4);

    /// <summary>
    /// The calendar month the quota resets on, in Azerbaijan local time rather than the server's
    /// UTC clock. B-1 fixes the period boundary to local time deliberately: for four hours around
    /// every UTC midnight-to-1-day-before-month-end, the UTC calendar and the Baku calendar disagree
    /// about which month it is, and a seller's fresh monthly slot should open when the month turns
    /// over for them, not for a server clock they never see.
    /// </summary>
    private DateOnly PeriodStart()
    {
        var local = clock.UtcNow.ToOffset(AzerbaijanOffset);
        return new DateOnly(local.Year, local.Month, 1);
    }

    private static string LimitReachedMessage(int limit) =>
        $"Bu kateqoriyada aylıq pulsuz elan limitinə çatmısınız ({limit}). Növbəti ay yenidən cəhd edin.";
}
