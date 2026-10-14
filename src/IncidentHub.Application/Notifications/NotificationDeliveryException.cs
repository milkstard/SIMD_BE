namespace IncidentHub.Application.Notifications;

/// <summary>
/// A send failed in a way a later attempt may fix (connection refused, timeout, 4xx reply, open circuit). Permanent failures throw
/// <see cref="Outbox.NonRetryableOutboxException"/> instead. The message is stored in <c>OutboxMessages.Error</c> and logged, so it
/// must not contain the recipient's address or the server's reply text.
/// </summary>
public sealed class NotificationDeliveryException : Exception
{
    public NotificationDeliveryException(string message)
        : base(message)
    {
    }

    public NotificationDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
