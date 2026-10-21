using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Infrastructure.Notifications.Chat;

/// <summary>Registered per chat channel when <c>Notifications:Chat:Enabled</c> is false: logs what would be posted and posts nothing. Never logs the URL.</summary>
internal sealed class LoggingChatSender(NotificationChannel channel, ILogger<LoggingChatSender> logger) : INotificationSender
{
    public NotificationChannel Channel => channel;

    public bool IsLive => false;

    public Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        var host = Uri.TryCreate(notification.ToAddress.Trim(), UriKind.Absolute, out var uri) ? uri.Host : "unknown";
        logger.LogInformation(
            "{Channel} posting is disabled; {OutboxMessageId} for a webhook at {RecipientHost} with subject {Subject} was not sent",
            channel,
            notification.MessageId,
            host,
            notification.Subject);
        return Task.CompletedTask;
    }
}
