using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents.Events;

/// <summary>Any change other than a status transition or an assignment (edit, severity change, SLA breach).</summary>
public sealed record IncidentUpdated(
    Guid IncidentId,
    IReadOnlyList<string> ChangedFields,
    Guid? ActorId,
    DateTimeOffset OccurredAt) : IDomainEvent;
