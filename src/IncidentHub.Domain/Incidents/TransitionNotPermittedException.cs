using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents;

/// <summary>The move exists, but the role or relation of the actor does not allow it. Mapped to 403.</summary>
public sealed class TransitionNotPermittedException(IncidentStatus from, IncidentStatus to)
    : DomainException($"You are not allowed to move an incident from {from} to {to}.")
{
    public IncidentStatus From { get; } = from;

    public IncidentStatus To { get; } = to;
}
