using IncidentHub.Domain.Notifications;

namespace IncidentHub.Application.Notifications.Dtos;

/// <summary>API-CONTRACT §5 <c>AppNotification</c>. Contract shape only; produced from BE-14 onwards.</summary>
public sealed record NotificationDto(
    Guid Id,
    NotificationType Type,
    NotificationIncidentRefDto Incident,
    string Message,
    bool IsRead,
    DateTimeOffset CreatedAt);

public sealed record NotificationIncidentRefDto(Guid Id, string Number, string Title);
