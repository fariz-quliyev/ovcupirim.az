namespace Ovcuprim.Domain.Enums;

/// <summary>
/// The on-site effect a <see cref="Ovcuprim.Domain.Entities.PromotionPackage"/> applies while active.
/// </summary>
/// <remarks>
/// Deliberately one value for phase 1. <see cref="Bump"/> is not a marketing name — it names the one
/// mechanism this phase implements: setting <c>Listing.BumpedAt</c> to now, which every existing sort
/// order already honours. A "featured" placement, a homepage slot, or any other effect is future work
/// that needs its own approved business definition and its own application logic before a second
/// value is added here — see docs/payment-integration-design.md, open decision #2.
/// </remarks>
public enum PromotionType
{
    /// <summary>Sets <c>Listing.BumpedAt</c> to the activation time for the package's duration.</summary>
    Bump = 0
}
