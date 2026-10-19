using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents.Events;

/// <summary>The incident reached the policy's escalation percentage of its resolve window. Raised once per SLA clock.</summary>
public sealed record SlaWarning(Guid IncidentId, DateTimeOffset OccurredAt) : IDomainEvent;
