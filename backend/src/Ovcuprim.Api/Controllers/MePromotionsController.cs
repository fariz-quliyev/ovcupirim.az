using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Payments;

namespace Ovcuprim.Api.Controllers;

/// <summary>
/// The seller's own promotion purchases. Every route resolves by the caller's own listing/order —
/// never by a supplied owner id — so there is nothing here for one seller to point at another's.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/me")]
[Produces("application/json")]
public sealed class MePromotionsController(IPromotionOrderService orders) : ApiControllerBase
{
    /// <summary>
    /// Starts a promotion purchase for the caller's own listing. The body carries a package id
    /// only — price is loaded server-side and is never accepted from the client.
    /// </summary>
    [HttpPost("listings/{listingId:guid}/promotions/orders")]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.PromotionOrderCreate)]
    [ProducesResponseType<CreatePromotionOrderResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatePromotionOrderResultDto>> CreateOrder(
        Guid listingId, CreatePromotionOrderRequest request, CancellationToken cancellationToken) =>
        FromResult(await orders.CreateOrderAsync(listingId, request, cancellationToken));

    [HttpGet("payment-orders/{id:guid}")]
    [ProducesResponseType<PaymentOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentOrderDto>> GetById(Guid id, CancellationToken cancellationToken) =>
        FromResult(await orders.GetMineByIdAsync(id, cancellationToken));

    [HttpGet("payment-orders")]
    [ProducesResponseType<PagedResult<PaymentOrderDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PaymentOrderDto>>> GetMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        FromResult(await orders.GetMineAsync(new PageRequest { Page = page, PageSize = pageSize }, cancellationToken));
}
