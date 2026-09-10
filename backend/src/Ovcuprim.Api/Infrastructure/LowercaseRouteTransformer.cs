using Microsoft.AspNetCore.Routing;

namespace Ovcuprim.Api.Infrastructure;

/// <summary>
/// Lowercases the <c>[controller]</c> token so every route is advertised the way clients already
/// call it.
/// </summary>
/// <remarks>
/// Route matching is case-insensitive, so <c>/api/v1/Listings</c> and <c>/api/v1/listings</c> both
/// worked before this. What did not match was the OpenAPI document, which advertised the
/// PascalCase spelling while the frontend used the lowercase one — and a case-sensitive cache in
/// front of the API would treat the two as separate entries. Only the spelling changes; no route
/// gains, loses or reshapes a segment.
/// </remarks>
public sealed class LowercaseRouteTransformer : IOutboundParameterTransformer
{
    public string? TransformOutbound(object? value) => value?.ToString()?.ToLowerInvariant();
}
