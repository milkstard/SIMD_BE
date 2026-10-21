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

    /// <summary>
    /// <c>false</c> for a sender that only logs (the channel is switched off). The router falls back to email instead of treating a
    /// log line as a delivery.
    /// </summary>
    bool IsLive { get; }

    Task SendAsync(Notification notification, CancellationToken cancellationToken);
}
