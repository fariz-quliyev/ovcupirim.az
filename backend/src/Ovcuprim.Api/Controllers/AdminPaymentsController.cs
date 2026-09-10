using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Payments;

namespace Ovcuprim.Api.Controllers;

/// <summary>
/// Promotion package management and payment-order reconciliation/refunds. Every action requires the
/// Admin role and writes an audit entry, the same discipline <c>AdminTaxonomyController</c> follows.
/// </summary>
[Authorize(Policy = AuthenticationSetup.Policies.Admin)]
[Route("api/v1/admin")]
[EnableRateLimiting(AuthenticationSetup.RateLimits.AdminAction)]
public sealed class AdminPaymentsController(
    IPromotionPackageAdminService packages,
    IPaymentAdminService payments) : ApiControllerBase
{
    /// <summary>Every package, including inactive ones the public catalog hides.</summary>
    [HttpGet("promotion-packages")]
    [ProducesResponseType<IReadOnlyList<AdminPromotionPackageDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminPromotionPackageDto>>> GetPackages(CancellationToken cancellationToken) =>
        AdminOk(await packages.GetAllAsync(cancellationToken));

    [HttpPost("promotion-packages")]
    [ProducesResponseType<AdminPromotionPackageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminPromotionPackageDto>> CreatePackage(
        CreatePromotionPackageRequest request, CancellationToken cancellationToken) =>
        AdminOk(await packages.CreateAsync(request, cancellationToken));

    [HttpPut("promotion-packages/{id:int}")]
    [ProducesResponseType<AdminPromotionPackageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminPromotionPackageDto>> UpdatePackage(
        int id, UpdatePromotionPackageRequest request, CancellationToken cancellationToken) =>
        AdminOk(await packages.UpdateAsync(id, request, cancellationToken));

    /// <summary>
    /// The reconciliation view — every payment order, optionally narrowed to one status and/or a
    /// search term (provider reference, listing number or title, seller name or phone).
    /// </summary>
    [HttpGet("payment-orders")]
    [ProducesResponseType<PagedResult<AdminPaymentOrderDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminPaymentOrderDto>>> GetOrders(
        [FromQuery] string? status,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        AdminOk(await payments.GetOrdersAsync(status, q, new PageRequest { Page = page, PageSize = pageSize }, cancellationToken));

    /// <summary>One order with the promotion it funded and its complete ledger.</summary>
    [HttpGet("payment-orders/{id:guid}")]
    [ProducesResponseType<AdminPaymentOrderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminPaymentOrderDetailDto>> GetOrder(Guid id, CancellationToken cancellationToken) =>
        AdminOk(await payments.GetOrderAsync(id, cancellationToken));

    /// <summary>
    /// Full or partial refund. Never leaves a refunded order's promotion active — the reversal is
    /// unconditional, per the approved state model.
    /// </summary>
    [HttpPost("payment-orders/{id:guid}/refund")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Refund(
        Guid id, RefundPaymentOrderRequest request, CancellationToken cancellationToken)
    {
        return AdminNoContent(await payments.RefundAsync(id, request, cancellationToken));
    }
}
