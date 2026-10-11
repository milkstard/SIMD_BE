using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents;

/// <summary>A partial edit of an incident. Properties left at their default are not touched.</summary>
public sealed record IncidentChanges
{
    public Optional<string> Title { get; init; }

    public Optional<string> Description { get; init; }

    public Optional<string?> StepsToReproduce { get; init; }

    public Optional<Priority?> Priority { get; init; }

    public Optional<Guid> ApplicationId { get; init; }

    public Optional<AppEnvironment> Environment { get; init; }
}
