namespace IncidentHub.Domain.Incidents.Sla;

/// <param name="WarnAt">When the resolve window reaches the policy's <c>EscalateAtPercent</c>.</param>
public readonly record struct SlaDueTimes(DateTimeOffset AckDueAt, DateTimeOffset ResolveDueAt, DateTimeOffset WarnAt);

/// <summary>Computes SLA due times. Pure: the start time is passed in because Domain has no clock.</summary>
public static class SlaClock
{
    public static SlaDueTimes DueTimes(SlaPolicy policy, DateTimeOffset from)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var start = from.ToUniversalTime();
        return new SlaDueTimes(
            start.AddMinutes(policy.AckMinutes),
            start.AddMinutes(policy.ResolveMinutes),
            start.AddMinutes(policy.ResolveMinutes * (double)policy.EscalateAtPercent / 100));
    }
}
