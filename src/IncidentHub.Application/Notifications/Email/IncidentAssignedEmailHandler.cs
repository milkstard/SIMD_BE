using IncidentHub.Application.Outbox;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Notifications.Email;

/// <summary>
/// A new assignee is mailed once. Re-assigning the same user, unassigning, a team-only change and self-assignment send nothing.
/// </summary>
public sealed class IncidentAssignedEmailHandler(
    IIncidentEmailReader reader,
    EmailDelivery delivery,
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

        var assignee = await reader.GetUserAsync(assigneeId, cancellationToken);
        if (assignee is null || string.IsNullOrWhiteSpace(assignee.Address))
        {
            logger.LogWarning(
                "Assigned email skipped: user {UserId} for incident {IncidentId} is missing or has no address",
                assigneeId,
                assigned.IncidentId);
            return;
        }

        await delivery.SendAsync(outboxMessageId, assignee, EmailContentBuilder.Assigned(incident), cancellationToken);
    }
}
