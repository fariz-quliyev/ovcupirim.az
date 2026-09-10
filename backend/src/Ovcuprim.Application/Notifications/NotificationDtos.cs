namespace Ovcuprim.Application.Notifications;

public sealed record NotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Body,
    string? EntityType,
    string? EntityId,
    bool IsRead,
    DateTimeOffset CreatedAt);

public sealed record UnreadNotificationCountDto(int Count);
