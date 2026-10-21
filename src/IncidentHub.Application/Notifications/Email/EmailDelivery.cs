using IncidentHub.Application.Abstractions;

namespace IncidentHub.Application.Notifications.Email;

/// <summary>
/// Renders an <see cref="EmailContent"/> and hands it to the email sender. The email leg of <see cref="NotificationRouter"/>. The outbox message id is the
/// notification's <see cref="Notification.MessageId"/>, so a redelivery carries the same <c>Message-Id</c>. Sender failures propagate:
/// the dispatcher owns retry and parking.
/// </summary>
public sealed class EmailDelivery(IEnumerable<INotificationSender> senders, IEmailTemplateRenderer renderer)
{
    public Task SendAsync(Guid outboxMessageId, EmailRecipient to, EmailContent content, CancellationToken cancellationToken)
    {
        var sender = senders.Single(s => s.Channel == NotificationChannel.Email);
        var body = renderer.Render(content.Headline, content.Facts, content.IncidentNumber);
        var notification = new Notification(outboxMessageId, to.Address.Trim(), to.DisplayName, content.Subject, body.Text, body.Html);
        return sender.SendAsync(notification, cancellationToken);
    }
}
