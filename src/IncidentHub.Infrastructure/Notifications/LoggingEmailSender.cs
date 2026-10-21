using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Infrastructure.Notifications;

/// <summary>Registered when <c>Notifications:Email:Enabled</c> is false: logs what would be sent and sends nothing.</summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public bool IsLive => false;

    public Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        var at = notification.ToAddress.LastIndexOf('@');
        var domain = at >= 0 ? notification.ToAddress[(at + 1)..] : "unknown";
        logger.LogInformation(
            "Email sending is disabled; {OutboxMessageId} for a recipient at {RecipientDomain} with subject {Subject} was not sent",
            notification.MessageId,
            domain,
            notification.Subject);
        return Task.CompletedTask;
    }
}
