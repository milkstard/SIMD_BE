using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents;

/// <summary>The target status is not reachable from the current status. Mapped to 409.</summary>
public sealed class InvalidTransitionException(IncidentStatus from, IncidentStatus to)
    : DomainException($"Cannot move an incident from {from} to {to}.")
{
    public IncidentStatus From { get; } = from;

    public IncidentStatus To { get; } = to;
}
