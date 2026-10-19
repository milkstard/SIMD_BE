using IncidentHub.Application.Outbox;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Notifications.Email;

/// <summary>
/// An SLA breach is mailed to the application's escalation contact. Without a contact (or when the contact has no address) it goes
/// to the owning team's address, so a breach always reaches someone.
/// </summary>
public sealed class SlaBreachedEmailHandler(
    IIncidentEmailReader reader,
    EmailDelivery delivery,
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

        var recipient = await ResolveRecipientAsync(incident, cancellationToken);
        if (recipient is null)
        {
            logger.LogWarning(
                "SLA breach email skipped: no escalation contact or team address for incident {IncidentId}",
                breached.IncidentId);
            return;
        }

        await delivery.SendAsync(outboxMessageId, recipient, EmailContentBuilder.SlaBreached(incident), cancellationToken);
    }

    private async Task<EmailRecipient?> ResolveRecipientAsync(IncidentEmailFacts incident, CancellationToken cancellationToken)
    {
        if (incident.EscalationUserId is { } contactId)
        {
            var contact = await reader.GetUserAsync(contactId, cancellationToken);
            if (contact is not null && !string.IsNullOrWhiteSpace(contact.Address))
            {
                return contact;
            }
        }

        var team = await reader.GetTeamAsync(incident.OwningTeamId, cancellationToken);
        if (team is null || string.IsNullOrWhiteSpace(team.Address))
        {
            return null;
        }

        logger.LogInformation(
            "SLA breach email for incident {IncidentNumber} goes to the owning team {TeamId}: no usable escalation contact",
            incident.Number,
            incident.OwningTeamId);
        return team;
    }
}
