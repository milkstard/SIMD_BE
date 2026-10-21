using IncidentHub.Application.Outbox;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Notifications.Email;

/// <summary>
/// A new assignee is notified once (by email, or on the team channel when they prefer Teams or Slack). Re-assigning the same user,
/// unassigning, a team-only change and self-assignment send nothing.
/// </summary>
public sealed class IncidentAssignedEmailHandler(
    IIncidentEmailReader reader,
    NotificationRouter router,
    ILogger<IncidentAssignedEmailHandler> logger) : IOutboxMessageHandler
{
    public string EventType => nameof(IncidentAssigned);

    public async Task HandleAsync(IDomainEvent @event, Guid outboxMessageId, CancellationToken cancellationToken)
    {
        if (@event is not IncidentAssigned assigned
            || assigned.AssigneeId is not { } assigneeId
            || assigneeId == assigned.PreviousAssigneeId
            || assigneeId == assigned.ActorId)
        {
            return;
        }

        var incident = await reader.GetIncidentAsync(assigned.IncidentId, cancellationToken);
        if (incident is null)
        {
            logger.LogWarning("Assigned email skipped: incident {IncidentId} no longer exists", assigned.IncidentId);
            return;
        }

        if (incident.AssigneeId != assigneeId)
        {
            logger.LogWarning(
                "Assigned email skipped: incident {IncidentId} is no longer assigned to user {UserId}",
                assigned.IncidentId,
                assigneeId);
            return;
        }

        var assignee = await reader.GetUserAsync(assigneeId, cancellationToken);
        if (assignee is null || string.IsNullOrWhiteSpace(assignee.Address))
        {
            logger.LogWarning(
                "Assigned email skipped: user {UserId} for incident {IncidentId} is missing or has no address",
                assigneeId,
                assigned.IncidentId);
            return;
        }

        await router.SendToUserAsync(
            outboxMessageId,
            assigneeId,
            assignee,
            incident.OwningTeamId,
            EmailContentBuilder.Assigned(incident),
            cancellationToken);
    }
}
