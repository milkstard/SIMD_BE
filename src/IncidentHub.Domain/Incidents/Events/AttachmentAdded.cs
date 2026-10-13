using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents.Events;

/// <summary>An upload was linked to an incident (report) or to one of its comments. Carries ids only.</summary>
public sealed record AttachmentAdded(
    Guid IncidentId,
    Guid AttachmentId,
    Guid? CommentId,
    bool IsInternal,
    Guid? ActorId,
    DateTimeOffset OccurredAt) : IDomainEvent;
