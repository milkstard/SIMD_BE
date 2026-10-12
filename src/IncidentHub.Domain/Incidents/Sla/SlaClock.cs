namespace IncidentHub.Domain.Incidents.Sla;

public readonly record struct SlaDueTimes(DateTimeOffset AckDueAt, DateTimeOffset ResolveDueAt);

/// <summary>Computes SLA due times. Pure: the start time is passed in because Domain has no clock.</summary>
public static class SlaClock
{
    public static SlaDueTimes DueTimes(SlaPolicy policy, DateTimeOffset from)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var start = from.ToUniversalTime();
        return new SlaDueTimes(start.AddMinutes(policy.AckMinutes), start.AddMinutes(policy.ResolveMinutes));
    }
}
