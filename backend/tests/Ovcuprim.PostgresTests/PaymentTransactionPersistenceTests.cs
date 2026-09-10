using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Notifications;
using Ovcuprim.Application.Payments;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.PostgresTests;

file sealed class NullCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public string? Role => null;
    public bool IsAuthenticated => false;
    public bool IsInRole(string role) => false;
}

/// <summary>
/// Accepts any well-formed field set as verified. Signature cryptography itself is covered in
/// Application.UnitTests against the real Epoint signing/verification code — this test project is
/// about what real PostgreSQL enforces, not about HMAC correctness.
/// </summary>
file sealed class StubPaymentGatewayClient : IPaymentGatewayClient
{
    public string ProviderName => "Epoint";

    public Func<string, PaymentGatewayStatusResult> NextStatusResult { get; set; } =
        reference => new(PaymentGatewayPaymentStatus.Paid, reference, null, "success", "000");

    public Task<PaymentGatewayOrderResult> CreateOrderAsync(
        PaymentGatewayOrderRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<PaymentGatewayStatusResult> GetOrderStatusAsync(
        string providerOrderReference, CancellationToken cancellationToken = default) =>
        Task.FromResult(NextStatusResult(providerOrderReference));

    public Task<PaymentGatewayRefundResult> RefundAsync(
        string providerOrderReference, decimal amountAzn, string reason, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public PaymentGatewayCallbackResult VerifyCallback(IReadOnlyDictionary<string, string> callbackFields) =>
        new(true, callbackFields["order_id"], callbackFields.GetValueOrDefault("transaction"),
            PaymentGatewayPaymentStatus.Paid, null, "success", "000");
}

/// <summary>
/// The idempotency guard PostgreSQL actually enforces on
/// <c>PaymentTransactions (PaymentOrderId, EventType, ProviderReference)</c>, and the "one active
/// promotion per listing" partial unique index on <c>Promotions.ListingId</c>.
/// </summary>
/// <remarks>
/// The in-memory provider used everywhere else does not enforce unique indexes at all — see
/// <c>ListingQuotaPersistenceTests</c> for the established rationale this project exists for. The
/// same reasoning applies here: <c>PaymentCallbackService.TryClaimCallbackAsync</c> names its own
/// failure mode by hand ("the unique index caught it"), and nothing else in the suite can prove that
/// index still exists and still does its job against a real database.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class PaymentTransactionPersistenceTests(PostgresFixture fixture)
{
    private sealed record World(Guid PaymentOrderId, Guid ListingId, int PackageId, decimal Amount);

    private async Task<World> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        var now = fixture.Clock.UtcNow;

        var seller = new User
        {
            Id = Guid.CreateVersion7(),
            PhoneNumber = $"+9945{Random.Shared.Next(10000000, 99999999)}",
            IsPhoneVerified = true,
            FullName = "Ödəniş Satıcısı",
            Role = UserRole.User,
            Status = UserStatus.Active,
            CreatedAt = now
        };

        var category = new Category
        {
            Slug = $"pay-{Guid.NewGuid():N}"[..20],
            NameAz = "Ödəniş",
            Depth = 0,
            SortOrder = 10,
            IsActive = true,
            RestrictionStatus = RestrictionStatus.Unrestricted,
            CreatedAt = now
        };

        var region = new Region
        {
            Slug = $"reg-{Guid.NewGuid():N}"[..20],
            NameAz = "Region",
            Type = RegionType.City,
            IsActive = true,
            IsSelectable = true,
            SortOrder = 10,
            CreatedAt = now
        };

        db.Users.Add(seller);
        db.Categories.Add(category);
        db.Regions.Add(region);
        await db.SaveChangesAsync();

        var listing = new Listing
        {
            Id = Guid.CreateVersion7(),
            Slug = $"listing-{Guid.NewGuid():N}"[..20],
            UserId = seller.Id,
            CategoryId = category.Id,
            RegionId = region.Id,
            Title = "Test elan",
            Description = "Test təsviri",
            Condition = ListingCondition.Used,
            SellerType = SellerType.Individual,
            ContactPhone = "+994501234567",
            Status = ListingStatus.Active,
            CreatedAt = now
        };
        db.Listings.Add(listing);

        var package = new PromotionPackage
        {
            Code = $"pkg-{Guid.NewGuid():N}"[..20],
            NameAz = "Paket",
            Type = PromotionType.Bump,
            DurationDays = 7,
            PriceAzn = 10m,
            Currency = "AZN",
            IsActive = true,
            SortOrder = 10,
            CreatedAt = now
        };
        db.PromotionPackages.Add(package);
        await db.SaveChangesAsync();

        var order = new PaymentOrder
        {
            Id = Guid.CreateVersion7(),
            SellerUserId = seller.Id,
            ListingId = listing.Id,
            PromotionPackageId = package.Id,
            AmountAzn = package.PriceAzn,
            Currency = "AZN",
            Status = PaymentOrderStatus.AwaitingPayment,
            Provider = "Epoint",
            ProviderOrderReference = $"prov-{Guid.NewGuid():N}",
            ExpiresAt = now.AddMinutes(30),
            CreatedAt = now
        };
        db.PaymentOrders.Add(order);

        var promotion = new Promotion
        {
            Id = Guid.CreateVersion7(),
            ListingId = listing.Id,
            PromotionPackageId = package.Id,
            PaymentOrderId = order.Id,
            Status = PromotionStatus.Pending,
            CreatedAt = now
        };
        db.Promotions.Add(promotion);

        await db.SaveChangesAsync();

        return new World(order.Id, listing.Id, package.Id, package.PriceAzn);
    }

    /// <summary>A second order/promotion pair for the same listing — the setup two racing purchases would leave behind.</summary>
    private async Task<Guid> SeedSecondOrderForSameListingAsync(World world)
    {
        await using var db = fixture.CreateContext();
        var now = fixture.Clock.UtcNow;

        var sellerId = await db.Listings.Where(l => l.Id == world.ListingId).Select(l => l.UserId).SingleAsync();

        var order = new PaymentOrder
        {
            Id = Guid.CreateVersion7(),
            SellerUserId = sellerId,
            ListingId = world.ListingId,
            PromotionPackageId = world.PackageId,
            AmountAzn = world.Amount,
            Currency = "AZN",
            Status = PaymentOrderStatus.AwaitingPayment,
            Provider = "Epoint",
            ProviderOrderReference = $"prov-{Guid.NewGuid():N}",
            ExpiresAt = now.AddMinutes(30),
            CreatedAt = now
        };
        db.PaymentOrders.Add(order);

        db.Promotions.Add(new Promotion
        {
            Id = Guid.CreateVersion7(),
            ListingId = world.ListingId,
            PromotionPackageId = world.PackageId,
            PaymentOrderId = order.Id,
            Status = PromotionStatus.Pending,
            CreatedAt = now
        });

        await db.SaveChangesAsync();

        return order.Id;
    }

    private PaymentCallbackService NewCallbackService(Infrastructure.Persistence.AppDbContext db, ICurrentUser currentUser) =>
        new(db, fixture.Clock,
            new StubPaymentGatewayClient { NextStatusResult = r => new(PaymentGatewayPaymentStatus.Paid, r, null, "success", "000") },
            new NotificationService(db, [new InAppNotificationChannel(db, fixture.Clock)], currentUser, fixture.Clock));

    private static IReadOnlyDictionary<string, string> CallbackFields(Guid orderId, string providerReference, decimal amount) =>
        new Dictionary<string, string>
        {
            ["order_id"] = orderId.ToString(),
            ["transaction"] = providerReference,
            ["status"] = "success",
            ["amount"] = amount.ToString(CultureInfo.InvariantCulture)
        };

    /// <summary>
    /// Drives a full race-then-recovery: two orders for one listing, the first activates and the
    /// second is genuinely paid but deferred (security audit finding B.2), then the first is reversed
    /// (as a refund would) and <see cref="PromotionRecoveryService"/> recovers the second. Shared by
    /// the recovery test and the post-recovery duplicate-callback test below.
    /// </summary>
    private async Task<(World World, Guid LoserOrderId)> SeedRacedAndRecoveredAsync()
    {
        var world = await SeedAsync();
        var loserOrderId = await SeedSecondOrderForSameListingAsync(world);
        var currentUser = new NullCurrentUser();

        // Sequential, not concurrent: the winner activates cleanly first, so the loser deterministically
        // hits TryActivatePromotionAsync's own pre-check. The DB-exception path under true concurrency
        // is what Two_orders_racing_for_the_same_listing... below proves separately.
        await using (var dbWinner = fixture.CreateContext())
        {
            var winnerResult = await NewCallbackService(dbWinner, currentUser)
                .ProcessCallbackAsync(CallbackFields(world.PaymentOrderId, $"win-{Guid.NewGuid():N}", world.Amount));
            Assert.True(winnerResult.Succeeded);
        }

        await using (var dbLoser = fixture.CreateContext())
        {
            var loserResult = await NewCallbackService(dbLoser, currentUser)
                .ProcessCallbackAsync(CallbackFields(loserOrderId, $"lose-{Guid.NewGuid():N}", world.Amount));
            Assert.True(loserResult.Succeeded);
        }

        // The winner's promotion is reversed — exactly what a refund does, and what frees the slot.
        await using (var dbReverse = fixture.CreateContext())
        {
            var winnerPromotion = await dbReverse.Promotions.SingleAsync(p => p.PaymentOrderId == world.PaymentOrderId);
            winnerPromotion.Status = PromotionStatus.Reversed;
            winnerPromotion.ReversedAt = fixture.Clock.UtcNow;
            winnerPromotion.ReversedReason = "Test: freeing the slot for recovery";
            await dbReverse.SaveChangesAsync();
        }

        await using (var dbRecover = fixture.CreateContext())
        {
            var recovery = new PromotionRecoveryService(
                dbRecover, fixture.Clock,
                new NotificationService(dbRecover, [new InAppNotificationChannel(dbRecover, fixture.Clock)], currentUser, fixture.Clock));

            var recoveredCount = await recovery.RecoverDeferredActivationsAsync();
            Assert.Equal(1, recoveredCount);
        }

        return (world, loserOrderId);
    }

    [Fact]
    public async Task A_resent_delivery_of_an_undecided_callback_still_reconciles_once_the_gateway_is_final()
    {
        if (!fixture.Available)
        {
            Assert.Fail(fixture.SkipReason);
        }

        var world = await SeedAsync();
        var providerReference = $"undecided-{Guid.NewGuid():N}";
        var currentUser = new NullCurrentUser();
        var fields = CallbackFields(world.PaymentOrderId, providerReference, world.Amount);

        // First delivery: the gateway's status endpoint still says "new". The delivery is claimed —
        // the idempotency row exists from now on — but nothing is decided (integration audit H-1).
        await using (var dbFirst = fixture.CreateContext())
        {
            var undecided = new PaymentCallbackService(dbFirst, fixture.Clock,
                new StubPaymentGatewayClient { NextStatusResult = r => new(PaymentGatewayPaymentStatus.Pending, r, null, "new", null) },
                new NotificationService(dbFirst, [new InAppNotificationChannel(dbFirst, fixture.Clock)], currentUser, fixture.Clock));

            var first = await undecided.ProcessCallbackAsync(fields);

            Assert.False(first.Succeeded);
            Assert.Equal(ResultError.Unavailable, first.Error);
        }

        // The gateway resends the identical delivery. The claim is refused by the real unique index —
        // and that refusal must not swallow the reconcile the still-open order needs.
        await using (var dbSecond = fixture.CreateContext())
        {
            var second = await NewCallbackService(dbSecond, currentUser).ProcessCallbackAsync(fields);

            Assert.True(second.Succeeded);
        }

        await using var verify = fixture.CreateContext();

        var order = await verify.PaymentOrders.SingleAsync(o => o.Id == world.PaymentOrderId);
        Assert.Equal(PaymentOrderStatus.Paid, order.Status);

        var promotion = await verify.Promotions.SingleAsync(p => p.PaymentOrderId == world.PaymentOrderId);
        Assert.Equal(PromotionStatus.Active, promotion.Status);

        var claims = await verify.PaymentTransactions.CountAsync(t =>
            t.PaymentOrderId == world.PaymentOrderId
            && t.EventType == PaymentEventType.CallbackReceived
            && t.ProviderReference == providerReference);
        Assert.Equal(1, claims);

        var checks = await verify.PaymentTransactions.CountAsync(t =>
            t.PaymentOrderId == world.PaymentOrderId && t.EventType == PaymentEventType.StatusChecked);
        Assert.Equal(2, checks);
    }

    [Fact]
    public async Task Two_concurrent_deliveries_of_the_same_callback_race_and_only_one_is_claimed()
    {
        if (!fixture.Available)
        {
            Assert.Fail(fixture.SkipReason);
        }

        var world = await SeedAsync();
        var providerReference = $"race-{Guid.NewGuid():N}";

        // Two independent contexts, exactly as two concurrent webhook deliveries — or a real retry
        // racing the original — would each get their own scoped services.
        await using var db1 = fixture.CreateContext();
        await using var db2 = fixture.CreateContext();

        var currentUser = new NullCurrentUser();
        var service1 = new PaymentCallbackService(
            db1, fixture.Clock,
            new StubPaymentGatewayClient { NextStatusResult = r => new(PaymentGatewayPaymentStatus.Paid, r, world.Amount, "success", "000") },
            new NotificationService(db1, [new InAppNotificationChannel(db1, fixture.Clock)], currentUser, fixture.Clock));

        var service2 = new PaymentCallbackService(
            db2, fixture.Clock,
            new StubPaymentGatewayClient { NextStatusResult = r => new(PaymentGatewayPaymentStatus.Paid, r, world.Amount, "success", "000") },
            new NotificationService(db2, [new InAppNotificationChannel(db2, fixture.Clock)], currentUser, fixture.Clock));

        var fields = CallbackFields(world.PaymentOrderId, providerReference, world.Amount);

        await Task.WhenAll(
            service1.ProcessCallbackAsync(fields),
            service2.ProcessCallbackAsync(fields));

        await using var verify = fixture.CreateContext();

        var claimed = await verify.PaymentTransactions
            .Where(t => t.PaymentOrderId == world.PaymentOrderId
                && t.EventType == PaymentEventType.CallbackReceived
                && t.ProviderReference == providerReference)
            .ToListAsync();

        // The unique index allows exactly one CallbackReceived row for this (order, reference) pair —
        // the mechanism that makes a re-delivered webhook a safe no-op instead of a double-activation.
        Assert.Single(claimed);

        var order = await verify.PaymentOrders.SingleAsync(o => o.Id == world.PaymentOrderId);
        Assert.Equal(PaymentOrderStatus.Paid, order.Status);

        var promotion = await verify.Promotions.SingleAsync(p => p.PaymentOrderId == world.PaymentOrderId);
        Assert.Equal(PromotionStatus.Active, promotion.Status);

        // Not double-activated, regardless of which of the two racing deliveries actually won the
        // claim. Scoped to this promotion's own id — this fixture's database is shared across every
        // test in the collection, so an unscoped count would also see rows other tests left behind.
        var activations = await verify.AuditLogs.CountAsync(
            a => a.Action == "promotion.activated" && a.EntityId == promotion.Id.ToString());
        Assert.Equal(1, activations);
    }

    [Fact]
    public async Task A_second_active_promotion_for_the_same_listing_is_refused_by_the_database_itself()
    {
        if (!fixture.Available)
        {
            Assert.Fail(fixture.SkipReason);
        }

        var world = await SeedAsync();

        await using var db = fixture.CreateContext();
        var promotion = await db.Promotions.SingleAsync(p => p.PaymentOrderId == world.PaymentOrderId);
        promotion.Status = PromotionStatus.Active;
        promotion.ActivatedAt = fixture.Clock.UtcNow;
        await db.SaveChangesAsync();

        // A second Active promotion for the SAME listing, written directly — bypassing
        // PromotionOrderService's own "already has an active promotion" check entirely — proves the
        // partial unique index on Promotions.ListingId, not just the service-layer check, refuses a
        // second one.
        db.Promotions.Add(new Promotion
        {
            Id = Guid.CreateVersion7(),
            ListingId = world.ListingId,
            PromotionPackageId = world.PackageId,
            Status = PromotionStatus.Active,
            ActivatedAt = fixture.Clock.UtcNow,
            CreatedAt = fixture.Clock.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Two_orders_racing_for_the_same_listing_leave_exactly_one_active_and_the_other_paid_but_deferred()
    {
        if (!fixture.Available)
        {
            Assert.Fail(fixture.SkipReason);
        }

        var world = await SeedAsync();
        var secondOrderId = await SeedSecondOrderForSameListingAsync(world);
        var currentUser = new NullCurrentUser();

        await using var db1 = fixture.CreateContext();
        await using var db2 = fixture.CreateContext();

        var fields1 = CallbackFields(world.PaymentOrderId, $"prov1-{Guid.NewGuid():N}", world.Amount);
        var fields2 = CallbackFields(secondOrderId, $"prov2-{Guid.NewGuid():N}", world.Amount);

        // True concurrency: this is what actually exercises TryActivatePromotionAsync's
        // DbUpdateException recovery path, not just its pre-check.
        var results = await Task.WhenAll(
            NewCallbackService(db1, currentUser).ProcessCallbackAsync(fields1),
            NewCallbackService(db2, currentUser).ProcessCallbackAsync(fields2));

        // Neither call may fail or throw — losing the promotion-slot race must never look like a
        // payment failure to whichever caller triggered it (security audit finding B.2).
        Assert.All(results, r => Assert.True(r.Succeeded));

        await using var verify = fixture.CreateContext();

        var orders = await verify.PaymentOrders
            .Where(o => o.Id == world.PaymentOrderId || o.Id == secondOrderId)
            .ToListAsync();

        Assert.Equal(2, orders.Count);
        // Central guarantee of the fix: both are genuinely Paid. Losing the promotion slot never
        // rolls back an already-verified payment.
        Assert.All(orders, o => Assert.Equal(PaymentOrderStatus.Paid, o.Status));

        var promotions = await verify.Promotions
            .Where(p => p.PaymentOrderId == world.PaymentOrderId || p.PaymentOrderId == secondOrderId)
            .ToListAsync();

        Assert.Equal(2, promotions.Count);
        Assert.Single(promotions, p => p.Status == PromotionStatus.Active);
        Assert.Single(promotions, p => p.Status == PromotionStatus.Pending);

        // The unique index is still the ultimate authority: never more than one Active row for this
        // listing, not even momentarily in a way this final read could catch.
        var activeCount = await verify.Promotions.CountAsync(
            p => p.ListingId == world.ListingId && p.Status == PromotionStatus.Active);
        Assert.Equal(1, activeCount);
    }

    [Fact]
    public async Task A_paid_order_whose_promotion_lost_the_race_is_recovered_once_the_winner_is_reversed()
    {
        if (!fixture.Available)
        {
            Assert.Fail(fixture.SkipReason);
        }

        var (world, loserOrderId) = await SeedRacedAndRecoveredAsync();

        await using var verify = fixture.CreateContext();

        var recoveredPromotion = await verify.Promotions.SingleAsync(p => p.PaymentOrderId == loserOrderId);
        Assert.Equal(PromotionStatus.Active, recoveredPromotion.Status);
        Assert.NotNull(recoveredPromotion.ActivatedAt);
        Assert.NotNull(recoveredPromotion.ExpiresAt);

        var listing = await verify.Listings.SingleAsync(l => l.Id == world.ListingId);
        Assert.Equal(recoveredPromotion.ActivatedAt, listing.BumpedAt);

        Assert.True(await verify.AuditLogs.AnyAsync(
            a => a.Action == "promotion.activated_on_recovery" && a.EntityId == recoveredPromotion.Id.ToString()));

        var loserOrder = await verify.PaymentOrders.SingleAsync(o => o.Id == loserOrderId);
        Assert.Equal(PaymentOrderStatus.Paid, loserOrder.Status);

        var notified = await verify.Notifications.AnyAsync(
            n => n.UserId == loserOrder.SellerUserId && n.Type == "promotion.activated");
        Assert.True(notified);
    }

    [Fact]
    public async Task A_duplicate_callback_after_recovery_does_not_reactivate_or_double_notify()
    {
        if (!fixture.Available)
        {
            Assert.Fail(fixture.SkipReason);
        }

        var (_, loserOrderId) = await SeedRacedAndRecoveredAsync();
        var currentUser = new NullCurrentUser();

        // A later, distinct delivery for the now-recovered order — exactly what a genuine gateway
        // retry, or a second webhook attempt after the recovery sweep, would look like.
        await using (var db = fixture.CreateContext())
        {
            var result = await NewCallbackService(db, currentUser)
                .ProcessCallbackAsync(CallbackFields(loserOrderId, $"post-recovery-{Guid.NewGuid():N}", 0m));
            Assert.True(result.Succeeded);
        }

        await using var verify = fixture.CreateContext();

        var promotion = await verify.Promotions.SingleAsync(p => p.PaymentOrderId == loserOrderId);
        Assert.Equal(PromotionStatus.Active, promotion.Status);

        // Exactly one activation on record — the recovery's own — regardless of what arrives afterward.
        var activations = await verify.AuditLogs.CountAsync(
            a => (a.Action == "promotion.activated" || a.Action == "promotion.activated_on_recovery")
                && a.EntityId == promotion.Id.ToString());
        Assert.Equal(1, activations);

        // Scoped to this specific order's own notification, not just the seller+type combination:
        // the winning order from the same race belongs to the same seller and rightly sent its own
        // "promotion.activated" notification too — that is a different, legitimate notification, not
        // a duplicate of this one.
        var notifications = await verify.Notifications.CountAsync(
            n => n.Type == "promotion.activated" && n.EntityId == loserOrderId.ToString());
        Assert.Equal(1, notifications);
    }
}
