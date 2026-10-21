using IncidentHub.Application.Outbox;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Notifications.Email;

/// <summary>A reported incident is mailed to the owning team's address and, when the team has a webhook, posted to its channel.</summary>
public sealed class IncidentReportedEmailHandler(
    IIncidentEmailReader reader,
    NotificationRouter router,
    ILogger<IncidentReportedEmailHandler> logger) : IOutboxMessageHandler
{
    public string EventType => nameof(IncidentReported);

    public async Task HandleAsync(IDomainEvent @event, Guid outboxMessageId, CancellationToken cancellationToken)
    {
        if (@event is not IncidentReported reported)
        {
            return;
        }

        var incident = await reader.GetIncidentAsync(reported.IncidentId, cancellationToken);
        if (incident is null)
        {
            logger.LogWarning("Reported email skipped: incident {IncidentId} no longer exists", reported.IncidentId);
            return;
        }

        var team = await reader.GetTeamAsync(incident.OwningTeamId, cancellationToken);
        if (team is null || string.IsNullOrWhiteSpace(team.Address))
        {
            logger.LogWarning(
                "Reported email skipped: team {TeamId} of incident {IncidentId} is missing or has no address",
                incident.OwningTeamId,
                reported.IncidentId);
            return;
        }

        await router.SendToTeamAsync(outboxMessageId, incident.OwningTeamId, team, EmailContentBuilder.Reported(incident), cancellationToken);
    }
}
