using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Payments;
using Ovcuprim.Application.UnitTests.Listings;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Payments;

/// <summary>
/// Builds on the listing harness for the same reason <c>StoreTestHarness</c> does: a payment order
/// is always about somebody's listing, so most of the world it needs already exists there.
/// </summary>
public sealed class PromotionTestHarness : IDisposable
{
    private readonly ListingTestHarness _listings = new();

    public PromotionTestHarness()
    {
        Gateway = new RecordingPaymentGatewayClient();
        PaymentOptions = Options.Create(new PaymentOptions { FrontendBaseUrl = "https://ovcupirim.test" });

        Packages = new PromotionPackageService(Db);
        PackageAdmin = new PromotionPackageAdminService(Db, CurrentUser, Clock);
        Orders = new PromotionOrderService(Db, CurrentUser, Clock, Gateway, PaymentOptions);
        Callbacks = new PaymentCallbackService(Db, Clock, Gateway, _listings.Notifications);
        Admin = new PaymentAdminService(Db, CurrentUser, Clock, Gateway, _listings.Notifications);
        Recovery = new PromotionRecoveryService(Db, Clock, _listings.Notifications);
    }

    public Infrastructure.Persistence.AppDbContext Db => _listings.Db;
    public Auth.FakeClock Clock => _listings.Clock;
    public Catalog.StubCurrentUser CurrentUser => _listings.CurrentUser;

    public RecordingPaymentGatewayClient Gateway { get; }
    public IOptions<PaymentOptions> PaymentOptions { get; }

    public IPromotionPackageService Packages { get; }
    public IPromotionPackageAdminService PackageAdmin { get; }
    public IPromotionOrderService Orders { get; }
    public IPaymentCallbackService Callbacks { get; }
    public IPaymentAdminService Admin { get; }
    public IPromotionRecoveryService Recovery { get; }

    public Guid SellerId => _listings.SellerId;

    public Task SeedAsync() => _listings.SeedAsync();

    public void ActAsSeller() => _listings.ActAsSeller();

    public void ActAsOtherUser() => _listings.ActAsOtherUser();

    public void ActAsAdmin()
    {
        CurrentUser.UserId = _listings.ModeratorId;
        CurrentUser.Role = "Admin";
    }

    /// <summary>A live, approved listing owned by the seller.</summary>
    public async Task<Guid> ApprovedListingAsync()
    {
        ActAsSeller();

        var created = await _listings.Listings.CreateDraftAsync(_listings.NewListing());
        var id = created.Value!.Id;

        await _listings.Media.AddAsync(id, ListingTestHarness.Upload());
        await _listings.Publishing.PublishAsync(id, new PublishListingRequest(false));

        _listings.ActAsModerator();
        await _listings.Moderation.ApproveAsync(id);
        ActAsSeller();

        return id;
    }

    /// <summary>
    /// A listing owned by the seller, in the requested non-Active status — for proving
    /// <c>PromotionOrderService.CreateOrderAsync</c> rejects all of them (security audit finding B.1).
    /// </summary>
    public async Task<Guid> ListingInStatusAsync(ListingStatus status)
    {
        ActAsSeller();

        var created = await _listings.Listings.CreateDraftAsync(_listings.NewListing());
        var id = created.Value!.Id;

        if (status == ListingStatus.Draft)
        {
            return id;
        }

        await _listings.Media.AddAsync(id, ListingTestHarness.Upload());
        await _listings.Publishing.PublishAsync(id, new PublishListingRequest(false));

        if (status == ListingStatus.PendingModeration)
        {
            return id;
        }

        if (status == ListingStatus.Rejected)
        {
            _listings.ActAsModerator();
            await _listings.Moderation.RejectAsync(id, "Test rədd səbəbi.");
            ActAsSeller();
            return id;
        }

        _listings.ActAsModerator();
        await _listings.Moderation.ApproveAsync(id);
        ActAsSeller();

        switch (status)
        {
            case ListingStatus.Active:
                return id;
            case ListingStatus.Blocked:
                _listings.ActAsModerator();
                await _listings.Moderation.BlockAsync(id, "Test bloklama səbəbi.");
                ActAsSeller();
                return id;
            case ListingStatus.Sold:
                await _listings.Listings.MarkSoldAsync(id);
                return id;
            case ListingStatus.Expired:
                // No service transition exists outside the maintenance sweep this phase relies on —
                // set directly, the same way that sweep itself would.
                var expiring = await Db.Listings.SingleAsync(l => l.Id == id);
                expiring.Status = ListingStatus.Expired;
                await Db.SaveChangesAsync();
                return id;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "No recipe for this status.");
        }
    }

    /// <summary>An active promotion package with the given price, ready to buy.</summary>
    public async Task<PromotionPackage> ActivePackageAsync(decimal priceAzn = 9.99m, int durationDays = 7, string code = "test-bump")
    {
        var package = new PromotionPackage
        {
            Code = code,
            NameAz = "Test paketi",
            Type = PromotionType.Bump,
            DurationDays = durationDays,
            PriceAzn = priceAzn,
            Currency = "AZN",
            IsActive = true,
            SortOrder = 10,
            CreatedAt = Clock.UtcNow
        };

        Db.PromotionPackages.Add(package);
        await Db.SaveChangesAsync();

        return package;
    }

    /// <summary>Creates an order for the seller's listing against the given package and returns it.</summary>
    public async Task<PaymentOrder> AwaitingPaymentOrderAsync(Guid listingId, PromotionPackage package)
    {
        ActAsSeller();

        var result = await Orders.CreateOrderAsync(listingId, new CreatePromotionOrderRequest(package.Id));

        return await Db.PaymentOrders.FindAsync(result.Value!.PaymentOrderId) ?? throw new InvalidOperationException();
    }

    public void Dispose() => _listings.Dispose();
}
