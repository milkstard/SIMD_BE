using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents;

/// <summary>A required or inconsistent input of a transition (note, close reason, duplicate target). Mapped to 422.</summary>
/// <param name="field">camelCase request field the message belongs to, e.g. <c>note</c>.</param>
/// <param name="message">What is wrong with that field.</param>
public sealed class TransitionRequirementException(string field, string message) : DomainException(message)
{
    public string Field { get; } = field;
}
