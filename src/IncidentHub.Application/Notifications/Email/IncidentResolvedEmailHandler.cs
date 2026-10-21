using IncidentHub.Application.Outbox;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Notifications.Email;

/// <summary>A move to <c>Resolved</c> is sent to the reporter, unless the reporter made it. Other transitions send nothing.</summary>
public sealed class IncidentResolvedEmailHandler(
    IIncidentEmailReader reader,
    NotificationRouter router,
    ILogger<IncidentResolvedEmailHandler> logger) : IOutboxMessageHandler
{
    public string EventType => nameof(IncidentTransitioned);

    public async Task HandleAsync(IDomainEvent @event, Guid outboxMessageId, CancellationToken cancellationToken)
    {
        if (@event is not IncidentTransitioned { To: IncidentStatus.Resolved } transitioned)
        {
            return;
        }

        var incident = await reader.GetIncidentAsync(transitioned.IncidentId, cancellationToken);
        if (incident is null)
        {
            logger.LogWarning("Resolved email skipped: incident {IncidentId} no longer exists", transitioned.IncidentId);
            return;
        }

        if (incident.ReporterId == transitioned.ActorId)
        {
            return;
        }

        var reporter = await reader.GetUserAsync(incident.ReporterId, cancellationToken);
        if (reporter is null || string.IsNullOrWhiteSpace(reporter.Address))
        {
            logger.LogWarning(
                "Resolved email skipped: reporter {UserId} of incident {IncidentId} is missing or has no address",
                incident.ReporterId,
                transitioned.IncidentId);
            return;
        }

        await router.SendToUserAsync(
            outboxMessageId,
            incident.ReporterId,
            reporter,
            incident.OwningTeamId,
            EmailContentBuilder.Resolved(incident, transitioned.Note),
            cancellationToken);
    }
}
