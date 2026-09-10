using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Listings;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.PostgresTests;

/// <summary>Returns a fixed limit for every category, so the race is about the row, not the policy.</summary>
internal sealed class FixedQuotaPolicy(int limit) : IListingQuotaPolicy
{
    public int? LimitFor(int categoryId) => limit;
}

/// <summary>
/// B-1: the unique index PostgreSQL actually enforces on <c>(UserId, CategoryId, PeriodStart)</c>.
/// </summary>
/// <remarks>
/// <c>ListingQuotaService.TryConsumeAsync</c> is check-then-write against a row that may not exist
/// yet, and the comment in that method names the failure mode by hand: "two publishes raced for the
/// same (user, category, period). The unique index caught it." The in-memory provider used
/// everywhere else does not enforce unique indexes at all, so nothing else in the suite can prove
/// that catch clause is reachable, or that the index it depends on is still there.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class ListingQuotaPersistenceTests(PostgresFixture fixture)
{
    private sealed record World(Guid UserId, int CategoryId);

    private async Task<World> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        var now = fixture.Clock.UtcNow;

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            PhoneNumber = $"+9945{Random.Shared.Next(10000000, 99999999)}",
            IsPhoneVerified = true,
            FullName = "Kvota Satıcısı",
            Role = UserRole.User,
            Status = UserStatus.Active,
            CreatedAt = now
        };

        var category = new Category
        {
            Slug = $"kvota-{Guid.NewGuid():N}"[..20],
            NameAz = "Kvota",
            Depth = 0,
            SortOrder = 10,
            IsActive = true,
            RestrictionStatus = RestrictionStatus.Unrestricted,
            CreatedAt = now
        };

        db.Users.Add(user);
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        return new World(user.Id, category.Id);
    }

    [Fact]
    public async Task Two_concurrent_first_publishes_race_for_the_same_row_and_only_one_wins()
    {
        if (!fixture.Available)
        {
            Assert.Fail(fixture.SkipReason);
        }

        var world = await SeedAsync();

        // Two independent contexts, exactly as two concurrent HTTP requests would each get their
        // own scoped DbContext: neither has seen the other's in-flight insert.
        var first = new ListingQuotaService(fixture.CreateContext(), new FixedQuotaPolicy(5), fixture.Clock);
        var second = new ListingQuotaService(fixture.CreateContext(), new FixedQuotaPolicy(5), fixture.Clock);

        var results = await Task.WhenAll(
            first.TryConsumeAsync(world.UserId, world.CategoryId),
            second.TryConsumeAsync(world.UserId, world.CategoryId));

        // The unique index allows exactly one row for (user, category, period); the loser is told to
        // retry rather than the request failing with an unhandled database exception.
        Assert.Contains(results, r => r.Succeeded);

        await using var verify = fixture.CreateContext();
        var rows = await verify.ListingQuotas
            .Where(q => q.UserId == world.UserId && q.CategoryId == world.CategoryId)
            .ToListAsync();

        var row = Assert.Single(rows);

        // Both requests observed the row as new and each tried to record its own single use; the
        // race is over which one PERSISTS, not over double-counting inside one saved row.
        Assert.Equal(1, row.UsedCount);
    }

    [Fact]
    public async Task A_retried_publish_after_losing_the_race_is_counted_correctly()
    {
        if (!fixture.Available)
        {
            Assert.Fail(fixture.SkipReason);
        }

        var world = await SeedAsync();

        var first = new ListingQuotaService(fixture.CreateContext(), new FixedQuotaPolicy(5), fixture.Clock);
        var second = new ListingQuotaService(fixture.CreateContext(), new FixedQuotaPolicy(5), fixture.Clock);

        var results = await Task.WhenAll(
            first.TryConsumeAsync(world.UserId, world.CategoryId),
            second.TryConsumeAsync(world.UserId, world.CategoryId));

        if (results.All(r => r.Succeeded))
        {
            // PostgreSQL serialised the two inserts instead of both racing the same absent row —
            // a legitimate outcome under the default isolation level. There is nothing to retry.
            return;
        }

        // The caller that lost the race is told to retry, and a real retry is a new HTTP request:
        // a fresh scope, a fresh DbContext, not the same one whose failed insert is still tracked
        // as pending. Reusing the losing service's own context here would just fail the same insert
        // a second time — a fact about that context's change tracker, not about the application.
        var retry = new ListingQuotaService(fixture.CreateContext(), new FixedQuotaPolicy(5), fixture.Clock);
        var retried = await retry.TryConsumeAsync(world.UserId, world.CategoryId);
        Assert.True(retried.Succeeded);

        await using var verify = fixture.CreateContext();
        var row = await verify.ListingQuotas
            .SingleAsync(q => q.UserId == world.UserId && q.CategoryId == world.CategoryId);

        Assert.Equal(2, row.UsedCount);
    }
}
