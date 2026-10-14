using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents.Events;

/// <summary>Any change other than a status transition or an assignment (edit, severity change, SLA breach).</summary>
/// <param name="PreviousApplicationId">The application the incident left; set only when <c>ApplicationId</c> changed.</param>
public sealed record IncidentUpdated(
    Guid IncidentId,
    IReadOnlyList<string> ChangedFields,
    Guid? ActorId,
    DateTimeOffset OccurredAt,
    Guid? PreviousApplicationId = null) : IDomainEvent;
