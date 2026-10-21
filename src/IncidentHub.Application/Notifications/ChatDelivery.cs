using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Notifications.Email;

namespace IncidentHub.Application.Notifications;

/// <summary>
/// Renders an <see cref="EmailContent"/> as plain text and hands it to the Teams or Slack sender, with the webhook URL as the
/// destination. The outbox message id is the notification's <see cref="Notification.MessageId"/>. Sender failures propagate.
/// </summary>
public sealed class ChatDelivery(IEnumerable<INotificationSender> senders, IEmailTemplateRenderer renderer)
{
    /// <summary>True when a sender for the channel is registered and really delivers (the channel is not switched off).</summary>
    public bool IsAvailable(NotificationChannel channel) => Find(channel) is not null;

    public Task SendAsync(Guid outboxMessageId, NotificationChannel channel, string webhookUrl, EmailContent content, CancellationToken cancellationToken)
    {
        var sender = Find(channel)
            ?? throw new InvalidOperationException($"No live {channel} sender is registered.");
        var body = renderer.Render(content.Headline, content.Facts, content.IncidentNumber);
        var notification = new Notification(outboxMessageId, webhookUrl.Trim(), null, content.Subject, body.Text, string.Empty);
        return sender.SendAsync(notification, cancellationToken);
    }

    private INotificationSender? Find(NotificationChannel channel) =>
        senders.FirstOrDefault(s => s.Channel == channel && s.IsLive);
}
