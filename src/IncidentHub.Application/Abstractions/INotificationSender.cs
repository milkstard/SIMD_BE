using IncidentHub.Application.Notifications;

namespace IncidentHub.Application.Abstractions;

/// <summary>
/// Delivers a <see cref="Notification"/> over one channel. Callers are outbox handlers only. Failure contract: throw
/// <see cref="NotificationDeliveryException"/> for a retryable failure and <see cref="Outbox.NonRetryableOutboxException"/> for a
/// permanent one. Implementations must be idempotent by <see cref="Notification.MessageId"/>.
/// </summary>
public interface INotificationSender
{
    NotificationChannel Channel { get; }

    Task SendAsync(Notification notification, CancellationToken cancellationToken);
}
