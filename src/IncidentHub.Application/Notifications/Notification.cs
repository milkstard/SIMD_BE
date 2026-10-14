namespace IncidentHub.Application.Notifications;

/// <summary>A channel-agnostic message for one recipient. Handlers build it; a sender delivers it.</summary>
/// <param name="MessageId">The outbox message id, used as the idempotency key (a redelivery carries the same id).</param>
public sealed record Notification
{
    public Notification(Guid messageId, string toAddress, string? toName, string subject, string textBody, string htmlBody)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("A message id is required.", nameof(messageId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(textBody);
        ArgumentNullException.ThrowIfNull(htmlBody);

        MessageId = messageId;
        ToAddress = toAddress;
        ToName = toName;
        Subject = subject;
        TextBody = textBody;
        HtmlBody = htmlBody;
    }

    public Guid MessageId { get; }

    public string ToAddress { get; }

    public string? ToName { get; }

    public string Subject { get; }

    public string TextBody { get; }

    public string HtmlBody { get; }
}
