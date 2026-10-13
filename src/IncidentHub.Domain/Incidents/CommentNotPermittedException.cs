using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents;

/// <summary>A non-responder tried to add an internal comment. Mapped to 403.</summary>
public sealed class CommentNotPermittedException()
    : DomainException("You are not allowed to add internal comments.");
