namespace Ovcuprim.Application.Payments;

/// <summary>
/// Where a paying customer is sent back to in their browser after checkout. These are UX
/// convenience redirects only — never trusted as proof of payment (docs/payment-integration-design.md,
/// section F). The gateway's own merchant-dashboard callback ("result_url" for Epoint) is configured
/// on the provider's side, not read from here.
/// </summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
}

/// <summary>Fixed policy for the payment/promotion flow — approved product numbers, not configuration.</summary>
public static class PromotionStateMachine
{
    /// <summary>
    /// Approved: an order that never reaches Paid within this window is swept to Expired and can
    /// never activate a promotion afterward, even if a late callback arrives.
    /// </summary>
    public static readonly TimeSpan PendingOrderLifetime = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How often an active promotion lifts its listing back to the top of the default ordering
    /// (integration audit L-3). Tap.az's "İrəli çək" is a repeated bump at a fixed interval for the
    /// paid duration, not a single one; a package's <c>DurationDays</c> therefore buys
    /// <c>DurationDays × 24 / 8</c> bumps, which is what its description promises. Applied by
    /// <c>PromotionMaintenanceService</c>; the first bump happens at activation.
    /// </summary>
    public static readonly TimeSpan BumpInterval = TimeSpan.FromHours(8);

    /// <summary>The same interval as a whole number of hours, for the public catalog and the seller UI.</summary>
    public static int BumpIntervalHours => (int)BumpInterval.TotalHours;
}
