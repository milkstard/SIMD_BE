using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents.Events;

/// <summary>The incident passed its resolve due time while still open. Raised once.</summary>
public sealed record SlaBreached(Guid IncidentId, DateTimeOffset OccurredAt) : IDomainEvent;
