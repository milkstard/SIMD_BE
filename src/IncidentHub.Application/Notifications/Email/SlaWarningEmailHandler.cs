using IncidentHub.Application.Outbox;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Notifications.Email;

/// <summary>
/// An SLA warning is mailed to the incident's current assignee. It sends nothing when the incident is gone or no longer open, when the
/// warning is not the incident's current one (a severity change restarted the SLA clock) or when nobody is assigned.
/// </summary>
public sealed class SlaWarningEmailHandler(
    IIncidentEmailReader reader,
    EmailDelivery delivery,
    ILogger<SlaWarningEmailHandler> logger) : IOutboxMessageHandler
{
    public string EventType => nameof(SlaWarning);

    public async Task HandleAsync(IDomainEvent @event, Guid outboxMessageId, CancellationToken cancellationToken)
    {
        if (@event is not SlaWarning warning)
        {
            return;
        }

        var incident = await reader.GetIncidentAsync(warning.IncidentId, cancellationToken);
        if (incident is null)
        {
            logger.LogWarning("SLA warning email skipped: incident {IncidentId} no longer exists", warning.IncidentId);
            return;
        }

        if (incident.Status is IncidentStatus.Resolved or IncidentStatus.Closed || incident.SlaWarnedAt != warning.OccurredAt)
        {
            logger.LogWarning("SLA warning email skipped: incident {IncidentId} is no longer in that warning", warning.IncidentId);
            return;
        }

        if (incident.AssigneeId is not { } assigneeId)
        {
            logger.LogWarning("SLA warning email skipped: incident {IncidentId} has no assignee", warning.IncidentId);
            return;
        }

        var assignee = await reader.GetUserAsync(assigneeId, cancellationToken);
        if (assignee is null || string.IsNullOrWhiteSpace(assignee.Address))
        {
            logger.LogWarning(
                "SLA warning email skipped: user {UserId} for incident {IncidentId} is missing or has no address",
                assigneeId,
                warning.IncidentId);
            return;
        }

        await delivery.SendAsync(outboxMessageId, assignee, EmailContentBuilder.SlaWarning(incident), cancellationToken);
    }
}
