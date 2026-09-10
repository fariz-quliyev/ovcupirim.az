using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;

namespace Ovcuprim.Application.Admin;

/// <summary>One row of the trail. The payload travels as parsed JSON, never as an opaque string.</summary>
public sealed record AuditEntryDto(
    Guid Id,
    Guid? ActorUserId,
    string? ActorName,
    string EntityType,
    string EntityId,
    string Action,
    JsonElement? Payload,
    DateTimeOffset CreatedAt);

/// <summary>What the viewer may narrow by. Every filter maps onto an index that already exists.</summary>
public sealed record AuditQuery
{
    public string? EntityType { get; init; }

    public string? EntityId { get; init; }

    public Guid? ActorUserId { get; init; }

    /// <summary>Matched as a prefix, so "store." selects every storefront decision.</summary>
    public string? Action { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }
}

public interface IAdminAuditService
{
    Task<Result<PagedResult<AuditEntryDto>>> QueryAsync(
        AuditQuery query, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>The trail for one entity, newest first. Used by the detail screens.</summary>
    Task<Result<IReadOnlyList<AuditEntryDto>>> ForEntityAsync(
        string entityType, string entityId, int limit = 20, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only access to the audit trail.
/// </summary>
/// <remarks>
/// There is deliberately no write, edit or delete here: <c>AuditLog</c> is append-only, and the
/// only thing an operator may do with it is read it. Access is Admin-only (PD-7.4) because payloads
/// carry free-text moderation reasons about identifiable people.
/// </remarks>
public sealed class AdminAuditService(IAppDbContext db) : IAdminAuditService
{
    /// <summary>A window is a window; asking for everything is not a filter.</summary>
    public const int MaxRange = 366;

    public async Task<Result<PagedResult<AuditEntryDto>>> QueryAsync(
        AuditQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        if (query.From is { } from && query.To is { } to && from > to)
        {
            return Result<PagedResult<AuditEntryDto>>.Invalid("from", "Başlanğıc tarix bitiş tarixindən sonra ola bilməz.");
        }

        if (query.From is { } start && query.To is { } end && (end - start).TotalDays > MaxRange)
        {
            return Result<PagedResult<AuditEntryDto>>.Invalid("to", $"Aralıq {MaxRange} gündən çox ola bilməz.");
        }

        var rows = Filtered(query);
        var total = await rows.CountAsync(cancellationToken);

        var items = await rows
            .OrderByDescending(a => a.CreatedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(a => new
            {
                a.Id,
                a.ActorUserId,
                ActorName = a.ActorUserId == null
                    ? null
                    : db.Users.Where(u => u.Id == a.ActorUserId).Select(u => u.FullName).FirstOrDefault(),
                a.EntityType,
                a.EntityId,
                a.Action,
                a.PayloadJson,
                a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var entries = items
            .Select(a => new AuditEntryDto(
                a.Id, a.ActorUserId, a.ActorName, a.EntityType, a.EntityId, a.Action,
                Parse(a.PayloadJson), a.CreatedAt))
            .ToList();

        return Result<PagedResult<AuditEntryDto>>.Success(
            new PagedResult<AuditEntryDto>(entries, page.Page, page.PageSize, total));
    }

    public async Task<Result<IReadOnlyList<AuditEntryDto>>> ForEntityAsync(
        string entityType, string entityId, int limit = 20, CancellationToken cancellationToken = default)
    {
        var result = await QueryAsync(
            new AuditQuery { EntityType = entityType, EntityId = entityId },
            new PageRequest { Page = 1, PageSize = Math.Clamp(limit, 1, PageRequest.MaxPageSize) },
            cancellationToken);

        return result.Succeeded
            ? Result<IReadOnlyList<AuditEntryDto>>.Success(result.Value!.Items)
            : Result<IReadOnlyList<AuditEntryDto>>.From(result);
    }

    private IQueryable<Domain.Entities.AuditLog> Filtered(AuditQuery query)
    {
        var rows = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            rows = rows.Where(a => a.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            rows = rows.Where(a => a.EntityId == query.EntityId);
        }

        if (query.ActorUserId is { } actorId)
        {
            rows = rows.Where(a => a.ActorUserId == actorId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var prefix = query.Action;
            rows = rows.Where(a => a.Action.StartsWith(prefix));
        }

        if (query.From is { } from)
        {
            rows = rows.Where(a => a.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(a => a.CreatedAt <= to);
        }

        return rows;
    }

    /// <summary>
    /// Payloads are structured JSON as of Phase 7. A row written before that — or any row that
    /// somehow is not JSON — comes back as null rather than taking the whole page down.
    /// </summary>
    private static JsonElement? Parse(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(payloadJson).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
