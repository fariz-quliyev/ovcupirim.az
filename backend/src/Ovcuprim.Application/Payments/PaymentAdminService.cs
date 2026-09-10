using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Notifications;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Payments;

public interface IPaymentAdminService
{
    /// <summary>
    /// The reconciliation view — every order, optionally narrowed to one status and/or a search
    /// term (a provider reference, a listing number or title, a seller's name or phone).
    /// </summary>
    Task<Result<PagedResult<AdminPaymentOrderDto>>> GetOrdersAsync(
        string? status, string? query, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>One order with the promotion it funded and its complete ledger (integration audit M-3).</summary>
    Task<Result<AdminPaymentOrderDetailDto>> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Staff-triggered refund. Never leaves a refunded order's promotion active — a full or partial
    /// refund always reverses it, per the approved state model.
    /// </summary>
    Task<Result> RefundAsync(Guid orderId, RefundPaymentOrderRequest request, CancellationToken cancellationToken = default);
}

public sealed class PaymentAdminService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IPaymentGatewayClient gateway,
    INotificationService notifications) : IPaymentAdminService
{
    public async Task<Result<PagedResult<AdminPaymentOrderDto>>> GetOrdersAsync(
        string? status, string? query, PageRequest page, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters is load-bearing (integration audit L-1): Listing's soft-delete filter
        // sits on a required navigation, and without this an order whose listing was since
        // soft-deleted would silently drop out of the page while still being counted in the total.
        // Financial records never disappear from a reconciliation view because of a listing's state.
        var orders = db.PaymentOrders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<PaymentOrderStatus>(status, ignoreCase: true, out var wanted))
            {
                return Result<PagedResult<AdminPaymentOrderDto>>.Invalid("status", "Naməlum status.");
            }

            orders = orders.Where(o => o.Status == wanted);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            var lowered = term.ToLowerInvariant();

            // A listing number is what a seller quotes in a complaint; a provider reference is what
            // the gateway's dashboard shows; a name or phone is how an operator finds a person. A
            // number is tried as all three — a phone fragment is a number too.
            var isNumber = long.TryParse(term, out var shortId);

            orders = orders.Where(o =>
                o.ProviderOrderReference == term
                || (isNumber && o.Listing.ShortId == shortId)
                || o.SellerUser.PhoneNumber.Contains(term)
                || o.Listing.Title.ToLower().Contains(lowered)
                || o.SellerUser.FullName.ToLower().Contains(lowered));
        }

        var ordered = orders.OrderByDescending(o => o.CreatedAt);
        var total = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(o => new AdminPaymentOrderDto(
                o.Id, o.SellerUserId, o.SellerUser.FullName, o.SellerUser.PhoneNumber,
                o.ListingId, o.Listing.ShortId, o.Listing.Title,
                o.PromotionPackage.NameAz, o.DurationDays > 0 ? o.DurationDays : o.PromotionPackage.DurationDays,
                o.AmountAzn, o.RefundedAmountAzn, o.Currency, o.Status.ToString(),
                o.Promotion != null ? o.Promotion.Status.ToString() : null,
                o.Provider, o.ProviderOrderReference, o.ExpiresAt, o.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<PagedResult<AdminPaymentOrderDto>>.Success(
            new PagedResult<AdminPaymentOrderDto>(items, page.Page, page.PageSize, total));
    }

    public async Task<Result<AdminPaymentOrderDetailDto>> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.PaymentOrders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(o => o.SellerUser)
            .Include(o => o.Listing)
            .Include(o => o.PromotionPackage)
            .Include(o => o.Promotion)
            .Include(o => o.Transactions.OrderBy(t => t.CreatedAt))
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return Result<AdminPaymentOrderDetailDto>.NotFound("Sifariş tapılmadı.");
        }

        var summary = new AdminPaymentOrderDto(
            order.Id, order.SellerUserId, order.SellerUser.FullName, order.SellerUser.PhoneNumber,
            order.ListingId, order.Listing.ShortId, order.Listing.Title,
            order.PromotionPackage.NameAz, PromotionDurations.For(order),
            order.AmountAzn, order.RefundedAmountAzn, order.Currency, order.Status.ToString(),
            order.Promotion?.Status.ToString(),
            order.Provider, order.ProviderOrderReference, order.ExpiresAt, order.CreatedAt);

        var promotion = order.Promotion is { } p
            ? new AdminPromotionDto(p.Id, p.Status.ToString(), p.ActivatedAt, p.ExpiresAt, p.ReversedAt, p.ReversedReason)
            : null;

        var transactions = order.Transactions
            .Select(t => new PaymentTransactionDto(
                t.Id, t.EventType.ToString(), t.ProviderReference, t.ProviderStatusRaw, t.AmountAzn, t.PayloadJson, t.CreatedAt))
            .ToList();

        return Result<AdminPaymentOrderDetailDto>.Success(new AdminPaymentOrderDetailDto(summary, promotion, transactions));
    }

    public async Task<Result> RefundAsync(
        Guid orderId, RefundPaymentOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.Invalid("reason", "Geri qaytarma səbəbi tələb olunur.");
        }

        var order = await db.PaymentOrders
            .IgnoreQueryFilters()
            .Include(o => o.Promotion)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return Result.NotFound("Sifariş tapılmadı.");
        }

        // Money that moved is always refundable: an ordinary capture, the remainder after a partial
        // refund, and a capture the gateway confirmed only after the order had expired (integration
        // audit M-1 — such an order never activates a promotion, so refunding is the only thing left).
        if (order.Status is not (PaymentOrderStatus.Paid or PaymentOrderStatus.PartiallyRefunded or PaymentOrderStatus.PaidAfterExpiry))
        {
            return Result.Conflict("Yalnız ödənilmiş sifariş geri qaytarıla bilər.");
        }

        if (order.ProviderOrderReference is not { } providerReference)
        {
            return Result.Conflict("Sifarişin ödəniş sistemi istinadı yoxdur.");
        }

        // Security audit finding B.3: validated against what actually remains after any earlier
        // refund, never against the original amount alone — otherwise a second partial refund could
        // pass the same "amount <= AmountAzn" check the first one did and together over-refund the
        // order. The client never supplies this remaining balance; it is entirely server-computed.
        var remaining = order.AmountAzn - order.RefundedAmountAzn;
        var amount = request.Amount ?? remaining;

        if (amount <= 0 || amount > remaining)
        {
            return Result.Invalid("amount", "Məbləğ qalıq geri qaytarıla bilən məbləğdən çox ola bilməz.");
        }

        var reason = request.Reason.Trim();
        var now = clock.UtcNow;
        var actorId = currentUser.UserId;

        PaymentAuditTrail.RecordTransaction(
            db, order.Id, PaymentEventType.RefundRequested, providerReference, null, amount, AuditPayload.Reason(reason), now);
        await db.SaveChangesAsync(cancellationToken);

        PaymentGatewayRefundResult refundResult;

        try
        {
            refundResult = await gateway.RefundAsync(providerReference, amount, reason, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            PaymentAuditTrail.RecordAudit(
                db, actorId, nameof(PaymentOrder), order.Id.ToString(), "payment_order.refund_failed",
                AuditPayload.From(new { error = ex.Message }), now);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Failure(ResultError.Conflict, "Geri qaytarma sorğusu uğursuz oldu.");
        }

        if (!refundResult.Succeeded)
        {
            PaymentAuditTrail.RecordAudit(
                db, actorId, nameof(PaymentOrder), order.Id.ToString(), "payment_order.refund_failed",
                AuditPayload.From(new { error = refundResult.ErrorMessage }), now);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Failure(ResultError.Conflict, refundResult.ErrorMessage ?? "Geri qaytarma rədd edildi.");
        }

        order.RefundedAmountAzn += amount;
        order.Status = order.RefundedAmountAzn >= order.AmountAzn
            ? PaymentOrderStatus.Refunded
            : PaymentOrderStatus.PartiallyRefunded;

        PaymentAuditTrail.RecordTransaction(
            db, order.Id, PaymentEventType.RefundConfirmed, refundResult.ProviderReference ?? providerReference,
            null, amount, AuditPayload.Reason(reason), now);

        // Approved rule: a refund — full or partial — always reverses the promotion. It never
        // re-enters Active afterward.
        if (order.Promotion is { Status: PromotionStatus.Active or PromotionStatus.Pending } promotion)
        {
            promotion.Status = PromotionStatus.Reversed;
            promotion.ReversedAt = now;
            promotion.ReversedReason = reason;

            PaymentAuditTrail.RecordAudit(
                db, actorId, nameof(Promotion), promotion.Id.ToString(), "promotion.reversed", AuditPayload.Reason(reason), now);
        }

        PaymentAuditTrail.RecordAudit(
            db, actorId, nameof(PaymentOrder), order.Id.ToString(), "payment_order.refunded",
            AuditPayload.From(new { amountAzn = amount }), now);

        // Staged before the save, not after: InAppNotificationChannel only adds the row to this same
        // context's change tracker — nothing else was ever going to flush it if it were staged later.
        await notifications.NotifyAsync(new NotificationMessage(
            order.SellerUserId, "promotion.reversed", "İrəli çəkmə ləğv edildi",
            "Ödənişiniz geri qaytarıldı və irəli çəkmə dayandırıldı.", nameof(PaymentOrder), order.Id.ToString()),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
