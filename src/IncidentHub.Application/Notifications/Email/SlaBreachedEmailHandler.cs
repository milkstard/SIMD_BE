using IncidentHub.Application.Outbox;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Notifications.Email;

/// <summary>
/// An SLA breach is sent to the application's escalation contact (email, or the team channel when they prefer Teams or Slack).
/// Without a contact (or when the contact has no address) it goes to the owning team's address and channel, so a breach always
/// reaches someone.
/// </summary>
public sealed class SlaBreachedEmailHandler(
    IIncidentEmailReader reader,
    NotificationRouter router,
    ILogger<SlaBreachedEmailHandler> logger) : IOutboxMessageHandler
{
    public string EventType => nameof(SlaBreached);

    public async Task HandleAsync(IDomainEvent @event, Guid outboxMessageId, CancellationToken cancellationToken)
    {
        if (@event is not SlaBreached breached)
        {
            return;
        }

        var incident = await reader.GetIncidentAsync(breached.IncidentId, cancellationToken);
        if (incident is null)
        {
            logger.LogWarning("SLA breach email skipped: incident {IncidentId} no longer exists", breached.IncidentId);
            return;
        }

        var (recipient, contactId) = await ResolveRecipientAsync(incident, cancellationToken);
        if (recipient is null)
        {
            logger.LogWarning(
                "SLA breach email skipped: no escalation contact or team address for incident {IncidentId}",
                breached.IncidentId);
            return;
        }

        var content = EmailContentBuilder.SlaBreached(incident);
        if (contactId is { } userId)
        {
            await router.SendToUserAsync(outboxMessageId, userId, recipient, incident.OwningTeamId, content, cancellationToken);
        }
        else
        {
            await router.SendToTeamAsync(outboxMessageId, incident.OwningTeamId, recipient, content, cancellationToken);
        }
    }

    /// <summary>The recipient and, when it is the escalation contact, that user's id (null for the owning-team fallback).</summary>
    private async Task<(EmailRecipient? Recipient, Guid? ContactId)> ResolveRecipientAsync(
        IncidentEmailFacts incident,
        CancellationToken cancellationToken)
    {
        if (incident.EscalationUserId is { } contactId)
        {
            var contact = await reader.GetUserAsync(contactId, cancellationToken);
            if (contact is not null && !string.IsNullOrWhiteSpace(contact.Address))
            {
                return (contact, contactId);
            }
        }

        var team = await reader.GetTeamAsync(incident.OwningTeamId, cancellationToken);
        if (team is null || string.IsNullOrWhiteSpace(team.Address))
        {
            return (null, null);
        }

        logger.LogInformation(
            "SLA breach email for incident {IncidentNumber} goes to the owning team {TeamId}: no usable escalation contact",
            incident.Number,
            incident.OwningTeamId);
        return (team, null);
    }
}
