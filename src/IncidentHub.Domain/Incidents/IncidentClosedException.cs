using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents;

/// <summary>A closed incident cannot be edited or reassigned. Mapped to 409.</summary>
public sealed class IncidentClosedException() : DomainException("The incident is closed and can no longer be changed.");
