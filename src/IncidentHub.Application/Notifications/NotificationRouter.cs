using IncidentHub.Application.Notifications.Email;
using IncidentHub.Application.Outbox;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Notifications;

/// <summary>
/// Chooses the leg(s) of a notification. A user who prefers Teams or Slack gets the post on the owning team's channel <i>instead of</i>
/// the email; when that channel cannot be used the email is sent so an alert is never lost. A team gets the email and, when it has a
/// webhook, the channel post as well. A transient chat failure propagates so the dispatcher retries the whole message.
/// <para>The webhook URL is a credential: it is never logged or put into an exception.</para>
/// </summary>
public sealed class NotificationRouter(
    EmailDelivery email,
    ChatDelivery chat,
    IIncidentEmailReader reader,
    ILogger<NotificationRouter> logger)
{
    public async Task SendToUserAsync(
        Guid outboxMessageId,
        Guid userId,
        EmailRecipient user,
        Guid owningTeamId,
        EmailContent content,
        CancellationToken cancellationToken)
    {
        var preferred = NotificationPrefsParser.Parse(user.NotificationPrefs);
        if (preferred is null)
        {
            logger.LogWarning("Notification preferences of user {UserId} are unreadable; using email", userId);
        }

        if (preferred is not (NotificationChannel.Teams or NotificationChannel.Slack))
        {
            await email.SendAsync(outboxMessageId, user, content, cancellationToken);
            return;
        }

        var channel = preferred.Value;
        var url = await reader.GetTeamChannelUrlAsync(owningTeamId, cancellationToken);
        var reason = !chat.IsAvailable(channel) ? "chat is disabled"
            : string.IsNullOrWhiteSpace(url) ? "the team has no webhook"
            : ChatWebhook.Classify(url) != channel ? $"the team webhook is not a {channel} webhook"
            : null;

        if (reason is not null)
        {
            logger.LogWarning(
                "User {UserId} prefers {Channel} but {Reason} (team {TeamId}); using email",
                userId,
                channel,
                reason,
                owningTeamId);
            await email.SendAsync(outboxMessageId, user, content, cancellationToken);
            return;
        }

        try
        {
            await chat.SendAsync(outboxMessageId, channel, url!, content, cancellationToken);
        }
        catch (NonRetryableOutboxException exception)
        {
            logger.LogWarning(
                "{Channel} post for user {UserId} (team {TeamId}) failed permanently: {Reason}; using email",
                channel,
                userId,
                owningTeamId,
                exception.Message);
            await email.SendAsync(outboxMessageId, user, content, cancellationToken);
        }
    }

    public async Task SendToTeamAsync(
        Guid outboxMessageId,
        Guid teamId,
        EmailRecipient team,
        EmailContent content,
        CancellationToken cancellationToken)
    {
        await email.SendAsync(outboxMessageId, team, content, cancellationToken);

        var url = await reader.GetTeamChannelUrlAsync(teamId, cancellationToken);
        if (ChatWebhook.Classify(url) is not { } channel)
        {
            return;
        }

        if (!chat.IsAvailable(channel))
        {
            logger.LogInformation("Team {TeamId} has a {Channel} webhook but chat is disabled; only the email was sent", teamId, channel);
            return;
        }

        try
        {
            await chat.SendAsync(outboxMessageId, channel, url!, content, cancellationToken);
        }
        catch (NonRetryableOutboxException exception)
        {
            // The mail went out; a revoked webhook must not park every message of this team.
            logger.LogWarning(
                "{Channel} post for team {TeamId} failed permanently: {Reason}; the email was sent",
                channel,
                teamId,
                exception.Message);
        }
    }
}
