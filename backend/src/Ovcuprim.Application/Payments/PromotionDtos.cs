using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Application.Payments;

/// <summary>
/// The public catalog entry — what a seller sees when choosing a package to buy.
/// <c>BumpIntervalHours</c> is how often the listing is lifted while the promotion runs — see
/// <see cref="PromotionStateMachine.BumpInterval"/> — so the UI can say what the duration buys.
/// </summary>
public sealed record PromotionPackageDto(
    int Id,
    string Code,
    string NameAz,
    string? DescriptionAz,
    int DurationDays,
    decimal PriceAzn,
    string Currency,
    int BumpIntervalHours);

/// <summary>The admin view — includes inactive packages and every editable field.</summary>
public sealed record AdminPromotionPackageDto(
    int Id,
    string Code,
    string NameAz,
    string? DescriptionAz,
    string Type,
    int DurationDays,
    decimal PriceAzn,
    string Currency,
    bool IsActive,
    int SortOrder);

public sealed record CreatePromotionPackageRequest(
    string Code,
    string NameAz,
    string? DescriptionAz,
    int DurationDays,
    decimal PriceAzn,
    int SortOrder);

public sealed record UpdatePromotionPackageRequest(
    string NameAz,
    string? DescriptionAz,
    int DurationDays,
    decimal PriceAzn,
    bool IsActive,
    int SortOrder);

/// <summary>The frontend sends a package id only — the server loads the authoritative price.</summary>
public sealed record CreatePromotionOrderRequest(int PackageId);

public sealed record CreatePromotionOrderResultDto(Guid PaymentOrderId, string RedirectUrl);

public sealed record PaymentOrderDto(
    Guid Id,
    Guid ListingId,
    int PromotionPackageId,
    string PackageNameAz,
    int DurationDays,
    decimal AmountAzn,
    string Currency,
    string Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record PromotionDto(
    Guid Id,
    Guid ListingId,
    string Status,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record RefundPaymentOrderRequest(decimal? Amount, string Reason);

/// <summary>The reconciliation view — every order, regardless of whose it is.</summary>
public sealed record AdminPaymentOrderDto(
    Guid Id,
    Guid SellerUserId,
    string SellerName,
    string SellerPhone,
    Guid ListingId,
    long ListingShortId,
    string ListingTitle,
    string PackageNameAz,
    int DurationDays,
    decimal AmountAzn,
    decimal RefundedAmountAzn,
    string Currency,
    string Status,
    string? PromotionStatus,
    string Provider,
    string? ProviderOrderReference,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);

/// <summary>The promotion an order funded, as an operator sees it.</summary>
public sealed record AdminPromotionDto(
    Guid Id,
    string Status,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ReversedAt,
    string? ReversedReason);

/// <summary>One row of the append-only ledger — see <see cref="PaymentTransaction"/>.</summary>
public sealed record PaymentTransactionDto(
    Guid Id,
    string EventType,
    string? ProviderReference,
    string? ProviderStatusRaw,
    decimal? AmountAzn,
    string? PayloadJson,
    DateTimeOffset CreatedAt);

/// <summary>
/// One order with everything an operator needs to reconcile or dispute it: the promotion it
/// funded and its complete ledger (integration audit M-3).
/// </summary>
public sealed record AdminPaymentOrderDetailDto(
    AdminPaymentOrderDto Order,
    AdminPromotionDto? Promotion,
    IReadOnlyList<PaymentTransactionDto> Transactions);

/// <summary>
/// The one place that answers "how long did this order buy". Orders created before the duration
/// snapshot existed carry 0 and fall back to the package's current value — the only value they
/// ever had.
/// </summary>
internal static class PromotionDurations
{
    public static int For(PaymentOrder order) =>
        order.DurationDays > 0 ? order.DurationDays : order.PromotionPackage.DurationDays;
}
