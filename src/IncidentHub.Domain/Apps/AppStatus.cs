namespace IncidentHub.Domain.Apps;

/// <summary>Derived, never stored: see <c>AppStatusCalculator</c> (BE-11).</summary>
public enum AppStatus
{
    Operational,
    Degraded,
    Outage,
}
