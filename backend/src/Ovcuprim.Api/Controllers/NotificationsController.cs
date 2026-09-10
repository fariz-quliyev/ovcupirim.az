using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Notifications;

namespace Ovcuprim.Api.Controllers;

/// <summary>"Bildirişlər" — what the site has told the signed-in account, and what they have read.</summary>
[ApiController]
[Authorize]
[Route("api/v1/me/notifications")]
[Produces("application/json")]
public sealed class NotificationsController(INotificationService notifications) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<NotificationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<NotificationDto>>> GetMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        FromResult(await notifications.GetMineAsync(
            new PageRequest { Page = page, PageSize = pageSize }, cancellationToken));

    /// <summary>The unread badge count. Cheap enough, and asked for often enough, to warrant its own route.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType<UnreadNotificationCountDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadNotificationCountDto>> GetUnreadCount(CancellationToken cancellationToken) =>
        FromResult(await notifications.GetUnreadCountAsync(cancellationToken));

    /// <summary>Idempotent: reading an already-read notification again is not an error.</summary>
    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var result = await notifications.MarkReadAsync(id, cancellationToken);
        return result.Succeeded ? NoContent() : Problem(result);
    }

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        var result = await notifications.MarkAllReadAsync(cancellationToken);
        return result.Succeeded ? NoContent() : Problem(result);
    }
}
