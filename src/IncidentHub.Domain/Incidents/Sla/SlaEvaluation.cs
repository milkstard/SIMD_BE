namespace IncidentHub.Domain.Incidents.Sla;

public enum SlaAction
{
    None,
    Warn,
    Breach,
}

/// <summary>Decides what the SLA monitor does with an incident. Pure: the current time is passed in.</summary>
public static class SlaEvaluation
{
    /// <summary>Every status except <c>Resolved</c> and <c>Closed</c>; <c>OnHold</c> still counts (wall-clock SLA).</summary>
    public static bool IsOpen(IncidentStatus status) => status is not (IncidentStatus.Resolved or IncidentStatus.Closed);

    /// <summary>Breach wins when both are due, so a monitor that was down does not send a stale warning.</summary>
    public static SlaAction Decide(Incident incident, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(incident);

        if (!IsOpen(incident.Status) || incident.IsSlaBreached)
        {
            return SlaAction.None;
        }

        if (incident.ResolveDueAt <= now)
        {
            return SlaAction.Breach;
        }

        return incident.SlaWarnedAt is null && incident.SlaWarnAt <= now ? SlaAction.Warn : SlaAction.None;
    }
}
