namespace IncidentHub.Domain.Notifications;

/// <summary>API-CONTRACT §2 <c>NotificationType</c>. Produced from BE-14 onwards.</summary>
public enum NotificationType
{
    Assigned,
    StatusChanged,
    Mentioned,
    SlaWarning,
    SlaBreached,
    Commented,
}
