using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents.Events;

public sealed record IncidentAssigned(
    Guid IncidentId,
    Guid? AssigneeId,
    Guid? AssignedTeamId,
    Guid? PreviousAssigneeId,
    Guid? PreviousAssignedTeamId,
    Guid? ActorId,
    DateTimeOffset OccurredAt) : IDomainEvent;
