using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Payments;

public interface IPromotionOrderService
{
    /// <summary>
    /// Creates a payment order for the caller's own listing and calls the gateway to open a hosted
    /// checkout. The frontend sends a package id only — the price is loaded server-side and is never
    /// accepted from the client.
    /// </summary>
    Task<Result<CreatePromotionOrderResultDto>> CreateOrderAsync(
        Guid listingId, CreatePromotionOrderRequest request, CancellationToken cancellationToken = default);

    Task<Result<PaymentOrderDto>> GetMineByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<PaymentOrderDto>>> GetMineAsync(
        PageRequest page, CancellationToken cancellationToken = default);
}

public sealed class PromotionOrderService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IPaymentGatewayClient gateway,
    IOptions<PaymentOptions> options) : IPromotionOrderService
{
    public async Task<Result<CreatePromotionOrderResultDto>> CreateOrderAsync(
        Guid listingId, CreatePromotionOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<CreatePromotionOrderResultDto>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var listing = await db.Listings.FirstOrDefaultAsync(l => l.Id == listingId, cancellationToken);

        // Ownership mismatch answers the same as a miss — the same shape every owner-scoped lookup
        // on this API uses, so a listing id can never be probed for existence or for who owns it.
        if (listing is null || listing.UserId != userId)
        {
            return Result<CreatePromotionOrderResultDto>.NotFound(ListingService.NotFoundMessage);
        }

        // Security audit finding B.1: ownership alone is not eligibility. A Draft, PendingModeration,
        // Rejected, Blocked, Sold or Expired listing is the caller's own, so this is a Conflict (the
        // seller can already see their own listing's status), not the NotFound anti-enumeration shape
        // above, which exists only to hide facts about listings the caller does not own.
        if (!ListingStateMachine.IsPubliclyVisible(listing.Status))
        {
            return Result<CreatePromotionOrderResultDto>.Conflict("Yalnız aktiv elan irəli çəkilə bilər.");
        }

        var package = await db.PromotionPackages
            .FirstOrDefaultAsync(p => p.Id == request.PackageId && p.IsActive, cancellationToken);

        if (package is null)
        {
            return Result<CreatePromotionOrderResultDto>.Invalid("packageId", "Paket tapılmadı.");
        }

        var now = clock.UtcNow;

        if (await db.Promotions.AnyAsync(
            p => p.ListingId == listingId && p.Status == PromotionStatus.Active, cancellationToken))
        {
            return Result<CreatePromotionOrderResultDto>.Conflict("Bu elan artıq irəli çəkilib.");
        }

        if (await db.PaymentOrders.AnyAsync(
            o => o.ListingId == listingId
                && (o.Status == PaymentOrderStatus.Created || o.Status == PaymentOrderStatus.AwaitingPayment)
                && o.ExpiresAt > now,
            cancellationToken))
        {
            return Result<CreatePromotionOrderResultDto>.Conflict(
                "Bu elan üçün ödəniş gözləyən sifariş artıq mövcuddur.");
        }

        var order = new PaymentOrder
        {
            Id = Guid.CreateVersion7(),
            SellerUserId = userId,
            ListingId = listingId,
            PromotionPackageId = package.Id,
            AmountAzn = package.PriceAzn,
            Currency = package.Currency,
            // Both halves of what the seller was shown are frozen here: the price and the duration.
            DurationDays = package.DurationDays,
            Status = PaymentOrderStatus.Created,
            Provider = gateway.ProviderName,
            ExpiresAt = now + PromotionStateMachine.PendingOrderLifetime,
            CreatedAt = now
        };

        var promotion = new Promotion
        {
            Id = Guid.CreateVersion7(),
            ListingId = listingId,
            PromotionPackageId = package.Id,
            PaymentOrderId = order.Id,
            Status = PromotionStatus.Pending,
            CreatedAt = now
        };

        db.PaymentOrders.Add(order);
        db.Promotions.Add(promotion);

        // Persisted before the gateway is ever called: the order id has to exist durably to be used
        // as the gateway's own order_id, and a network failure calling out must not lose the row —
        // it is instead recorded Failed below.
        await db.SaveChangesAsync(cancellationToken);

        var baseUrl = options.Value.FrontendBaseUrl.TrimEnd('/');

        PaymentGatewayOrderResult gatewayResult;

        try
        {
            gatewayResult = await gateway.CreateOrderAsync(
                new PaymentGatewayOrderRequest(
                    OrderId: order.Id.ToString(),
                    AmountAzn: order.AmountAzn,
                    Description: $"OvcuPrim.az — {package.NameAz}",
                    SuccessRedirectUrl: $"{baseUrl}/promotions/orders/{order.Id}/return?outcome=success",
                    ErrorRedirectUrl: $"{baseUrl}/promotions/orders/{order.Id}/return?outcome=error"),
                cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            await FailOrderAsync(order, ex.Message, now, cancellationToken);
            return Result<CreatePromotionOrderResultDto>.Failure(
                ResultError.Conflict, "Ödəniş sistemi ilə əlaqə qurula bilmədi. Bir az sonra yenidən cəhd edin.");
        }

        if (!gatewayResult.Succeeded || string.IsNullOrWhiteSpace(gatewayResult.RedirectUrl))
        {
            await FailOrderAsync(order, gatewayResult.ErrorMessage ?? "Naməlum xəta.", now, cancellationToken);
            return Result<CreatePromotionOrderResultDto>.Failure(
                ResultError.Conflict, "Ödəniş sifarişi yaradıla bilmədi.");
        }

        order.ProviderOrderReference = gatewayResult.ProviderOrderReference;
        order.Status = PaymentOrderStatus.AwaitingPayment;

        PaymentAuditTrail.RecordTransaction(
            db, order.Id, PaymentEventType.OrderCreated, gatewayResult.ProviderOrderReference,
            null, order.AmountAzn, AuditPayload.From(new { provider = order.Provider }), now);

        PaymentAuditTrail.RecordAudit(
            db, userId, nameof(PaymentOrder), order.Id.ToString(), "payment_order.created",
            AuditPayload.From(new { listingId, packageId = package.Id, amountAzn = order.AmountAzn }), now);

        await db.SaveChangesAsync(cancellationToken);

        return Result<CreatePromotionOrderResultDto>.Success(
            new CreatePromotionOrderResultDto(order.Id, gatewayResult.RedirectUrl));
    }

    public async Task<Result<PaymentOrderDto>> GetMineByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<PaymentOrderDto>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        // Owner-scoped in the query itself, not checked afterward — the same anti-enumeration shape
        // every owner-scoped lookup on this API uses.
        var order = await db.PaymentOrders.AsNoTracking()
            .Include(o => o.PromotionPackage)
            .FirstOrDefaultAsync(o => o.Id == id && o.SellerUserId == userId, cancellationToken);

        return order is null
            ? Result<PaymentOrderDto>.NotFound("Sifariş tapılmadı.")
            : Result<PaymentOrderDto>.Success(ToDto(order));
    }

    public async Task<Result<PagedResult<PaymentOrderDto>>> GetMineAsync(
        PageRequest page, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result<PagedResult<PaymentOrderDto>>.Failure(ResultError.Unauthorized, "Giriş tələb olunur.");
        }

        var query = db.PaymentOrders.AsNoTracking()
            .Include(o => o.PromotionPackage)
            .Where(o => o.SellerUserId == userId)
            .OrderByDescending(o => o.CreatedAt);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return Result<PagedResult<PaymentOrderDto>>.Success(
            new PagedResult<PaymentOrderDto>(items.Select(ToDto).ToList(), page.Page, page.PageSize, total));
    }

    private async Task FailOrderAsync(PaymentOrder order, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        order.Status = PaymentOrderStatus.Failed;

        PaymentAuditTrail.RecordTransaction(
            db, order.Id, PaymentEventType.OrderCreated, null, "gateway_error", null,
            AuditPayload.From(new { error = reason }), now);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static PaymentOrderDto ToDto(PaymentOrder order) => new(
        order.Id, order.ListingId, order.PromotionPackageId, order.PromotionPackage.NameAz,
        PromotionDurations.For(order), order.AmountAzn, order.Currency, order.Status.ToString(),
        order.ExpiresAt, order.CreatedAt);
}
