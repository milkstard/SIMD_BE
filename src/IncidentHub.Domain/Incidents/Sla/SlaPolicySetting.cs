namespace IncidentHub.Domain.Incidents.Sla;

/// <summary>
/// The stored response targets of one severity (a row of <c>SlaPolicies</c>, keyed by <see cref="Severity"/>).
/// <see cref="ToPolicy"/> yields the <see cref="SlaPolicy"/> value that <see cref="SlaClock"/> uses.
/// </summary>
public sealed class SlaPolicySetting
{
    /// <summary>One year; keeps the due-time arithmetic on <see cref="DateTimeOffset"/> safe.</summary>
    public const int MaxResolveMinutes = 525_600;

    private SlaPolicySetting()
    {
    }

    public Severity Severity { get; private set; }

    public int AckMinutes { get; private set; }

    public int ResolveMinutes { get; private set; }

    public int EscalateAtPercent { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static SlaPolicySetting Create(Severity severity, int ackMinutes, int resolveMinutes, int escalateAtPercent)
    {
        var setting = new SlaPolicySetting { Severity = severity };
        setting.Update(ackMinutes, resolveMinutes, escalateAtPercent);
        return setting;
    }

    public void Update(int ackMinutes, int resolveMinutes, int escalateAtPercent)
    {
        // The SlaPolicy constructor owns the invariants (ack > 0, resolve >= ack, percent range); only the upper bound is added here.
        var policy = new SlaPolicy(Severity, ackMinutes, resolveMinutes, escalateAtPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(resolveMinutes, MaxResolveMinutes);

        AckMinutes = policy.AckMinutes;
        ResolveMinutes = policy.ResolveMinutes;
        EscalateAtPercent = policy.EscalateAtPercent;
    }

    public SlaPolicy ToPolicy() => new(Severity, AckMinutes, ResolveMinutes, EscalateAtPercent);
}
