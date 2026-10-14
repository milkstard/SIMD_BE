using IncidentHub.Domain.Incidents;

namespace IncidentHub.Domain.Apps;

/// <summary>One open incident as the calculator sees it. Which incidents are "open" is decided by the caller's query.</summary>
public readonly record struct OpenIncidentFact(Severity Severity, AppEnvironment Environment, DateTimeOffset CreatedAt);

/// <param name="Since">Earliest creation time among the open incidents that cause the status; null when Operational.</param>
public readonly record struct AppStatusResult(AppStatus Status, int OpenIncidents, Severity? WorstOpenSeverity, DateTimeOffset? Since);

/// <summary>
/// Derives the status of an application from its open incidents. Open Critical in Production → Outage; any other open
/// Critical or High → Degraded; otherwise Operational. The status is never stored or set by a user.
/// </summary>
public static class AppStatusCalculator
{
    public static AppStatusResult Calculate(IEnumerable<OpenIncidentFact> openIncidents)
    {
        ArgumentNullException.ThrowIfNull(openIncidents);

        var facts = openIncidents.ToList();
        if (facts.Count == 0)
        {
            return new AppStatusResult(AppStatus.Operational, 0, null, null);
        }

        // Severity values grow from most to least severe (Critical = 1).
        var worst = facts.Min(f => f.Severity);

        var outage = facts.Where(f => f.Severity == Severity.Critical && f.Environment == AppEnvironment.Production).ToList();
        if (outage.Count > 0)
        {
            return new AppStatusResult(AppStatus.Outage, facts.Count, worst, outage.Min(f => f.CreatedAt));
        }

        var degraded = facts.Where(f => f.Severity is Severity.Critical or Severity.High).ToList();
        return degraded.Count > 0
            ? new AppStatusResult(AppStatus.Degraded, facts.Count, worst, degraded.Min(f => f.CreatedAt))
            : new AppStatusResult(AppStatus.Operational, facts.Count, worst, null);
    }
}
