using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Listings;

/// <summary>One thing a screener wants a moderator to look at. Never blocks publication by itself.</summary>
public sealed record ScreeningFlag(string Code, string Message);

public sealed record ScreeningResult(IReadOnlyList<ScreeningFlag> Flags)
{
    public bool IsClean => Flags.Count == 0;

    public static ScreeningResult Clean { get; } = new([]);
}

public sealed record ListingScreeningInput(
    Guid ListingId,
    Guid UserId,
    int CategoryId,
    string Title,
    string Description,
    decimal? Price);

/// <summary>
/// Where prohibited-item and duplicate policy plug in. The mechanism ships complete; a
/// prohibited-item policy is deliberately absent — that is legal content, not mechanism, and
/// nothing here invents it (see docs/screening-blockers.md). What ships today is the one rule that
/// is purely mechanical rather than a legal judgement: duplicate detection.
/// </summary>
public interface IContentScreener
{
    Task<ScreeningResult> ScreenAsync(ListingScreeningInput input, CancellationToken cancellationToken = default);
}

/// <summary>Flags nothing. Every listing still goes to a moderator, so the fail-safe is unchanged.</summary>
public sealed class NoOpContentScreener : IContentScreener
{
    public Task<ScreeningResult> ScreenAsync(ListingScreeningInput input, CancellationToken cancellationToken = default) =>
        Task.FromResult(ScreeningResult.Clean);
}

/// <summary>
/// One independently pluggable screening rule. Each policy owns one concern and returns the flags
/// it finds; it never rejects or approves anything itself — Rule 6 (nothing goes live without a
/// moderator) is enforced once, upstream of every screener, and no policy gets a way around it.
/// </summary>
/// <remarks>
/// This is the seam a future prohibited-item policy plugs into: a second <see cref="IScreeningPolicy"/>
/// registered alongside <see cref="DuplicateListingScreeningPolicy"/>, reading whatever wordlist or
/// classification table the legal decision turns out to require. Nothing about the mechanism changes
/// to add it — which is the point of shipping the seam now and the content later.
/// </remarks>
public interface IScreeningPolicy
{
    /// <summary>Short, stable, machine-readable — becomes the audit payload's flag code and the moderation UI's badge.</summary>
    string Code { get; }

    Task<IReadOnlyList<ScreeningFlag>> EvaluateAsync(ListingScreeningInput input, CancellationToken cancellationToken = default);
}

/// <summary>Runs every registered policy and pools what they find. Order does not matter: nothing here is exclusive.</summary>
public sealed class CompositeContentScreener(IEnumerable<IScreeningPolicy> policies) : IContentScreener
{
    public async Task<ScreeningResult> ScreenAsync(ListingScreeningInput input, CancellationToken cancellationToken = default)
    {
        var flags = new List<ScreeningFlag>();

        foreach (var policy in policies)
        {
            flags.AddRange(await policy.EvaluateAsync(input, cancellationToken));
        }

        return flags.Count == 0 ? ScreeningResult.Clean : new ScreeningResult(flags);
    }
}

/// <summary>
/// Every policy's own configuration lives under its own key, isolated from the rate limits and the
/// quota above it — screening is not a security control and not a monetisation number, and mixing
/// its knobs into either section would make a future policy's configuration harder to find, not
/// easier. Bound from <see cref="SectionName"/> with the defaults below applied to an unconfigured
/// host, the same "the number in the code is what production gets" convention as every other
/// approved default on this API.
/// </summary>
public sealed class ScreeningOptions
{
    public const string SectionName = "Listings:Screening";

    /// <summary>On by default: it is mechanical, not a legal judgement, and flagging never blocks anything.</summary>
    public bool DuplicateDetectionEnabled { get; set; } = true;

    /// <summary>How far back to look for a title the same seller already has live or pending in the same category.</summary>
    public int DuplicateLookbackDays { get; set; } = 30;
}

/// <summary>
/// Flags a submission whose folded title exactly matches another listing the same seller already
/// has live or awaiting a decision in the same category.
/// </summary>
/// <remarks>
/// Deliberately the narrowest rule that is still useful, and deliberately mechanical rather than a
/// judgement call: exact match on the same folded text already used for search (see
/// <see cref="AzerbaijaniText"/>), not a similarity score with a tunable threshold — a threshold is
/// itself a policy decision, and "how similar is too similar" is exactly the kind of duplicate
/// enforcement policy this ships without inventing. A near-duplicate a moderator would recognise on
/// sight is exactly what the moderation queue is for.
/// </remarks>
public sealed class DuplicateListingScreeningPolicy(
    IAppDbContext db, IOptions<ScreeningOptions> options, IDateTimeProvider clock) : IScreeningPolicy
{
    public string Code => "duplicate";

    public async Task<IReadOnlyList<ScreeningFlag>> EvaluateAsync(
        ListingScreeningInput input, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (!settings.DuplicateDetectionEnabled)
        {
            return [];
        }

        var since = clock.UtcNow - TimeSpan.FromDays(settings.DuplicateLookbackDays);

        // Filtered in SQL by everything translatable; the folded-text comparison itself runs in
        // memory over what is, in practice, a handful of rows — one seller's own recent submissions
        // in one category.
        var candidates = await db.Listings.AsNoTracking()
            .Where(l => l.UserId == input.UserId
                && l.CategoryId == input.CategoryId
                && l.Id != input.ListingId
                && (l.Status == ListingStatus.PendingModeration || l.Status == ListingStatus.Active)
                && l.CreatedAt >= since)
            .Select(l => l.Title)
            .ToListAsync(cancellationToken);

        var normalizedTitle = AzerbaijaniText.Normalize(input.Title);

        var isDuplicate = candidates.Any(title =>
            string.Equals(AzerbaijaniText.Normalize(title), normalizedTitle, StringComparison.Ordinal));

        return isDuplicate
            ? [new ScreeningFlag(Code, "Bu başlıqla bu kateqoriyada artıq bir elanınız var.")]
            : [];
    }
}
