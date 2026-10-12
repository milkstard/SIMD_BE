namespace IncidentHub.Domain.Incidents.Sla;

/// <summary>Response targets for one severity. Minutes are wall-clock (24x7).</summary>
public sealed record SlaPolicy
{
    public const int MinEscalatePercent = 1;
    public const int MaxEscalatePercent = 99;

    public SlaPolicy(Severity severity, int ackMinutes, int resolveMinutes, int escalateAtPercent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ackMinutes, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(resolveMinutes, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(resolveMinutes, ackMinutes);
        ArgumentOutOfRangeException.ThrowIfLessThan(escalateAtPercent, MinEscalatePercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(escalateAtPercent, MaxEscalatePercent);

        Severity = severity;
        AckMinutes = ackMinutes;
        ResolveMinutes = resolveMinutes;
        EscalateAtPercent = escalateAtPercent;
    }

    public Severity Severity { get; }

    public int AckMinutes { get; }

    public int ResolveMinutes { get; }

    public int EscalateAtPercent { get; }
}
