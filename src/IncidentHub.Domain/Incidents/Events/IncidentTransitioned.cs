using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents.Events;

public sealed record IncidentTransitioned(
    Guid IncidentId,
    IncidentStatus From,
    IncidentStatus To,
    Guid? ActorId,
    string? Note,
    DateTimeOffset OccurredAt) : IDomainEvent;
