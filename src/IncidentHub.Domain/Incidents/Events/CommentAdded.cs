using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents.Events;

/// <summary>Carries ids only; the body is never copied into the outbox.</summary>
public sealed record CommentAdded(
    Guid IncidentId,
    Guid CommentId,
    bool IsInternal,
    Guid? ActorId,
    DateTimeOffset OccurredAt) : IDomainEvent;
