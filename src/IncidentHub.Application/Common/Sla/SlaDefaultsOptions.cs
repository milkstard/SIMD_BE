using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;

namespace IncidentHub.Application.Common.Sla;

/// <summary>
/// Configuration section <c>Sla:Defaults</c>. Every entry starts with the built-in default, so the configuration binder
/// only overrides the values that are supplied and a missing entry falls back to the default.
/// </summary>
public sealed class SlaDefaultsOptions
{
    public const string SectionName = "Sla:Defaults";

    public SlaTargetOptions Critical { get; set; } = new() { AckMinutes = 15, ResolveMinutes = 240, EscalateAtPercent = 80 };

    public SlaTargetOptions High { get; set; } = new() { AckMinutes = 60, ResolveMinutes = 480, EscalateAtPercent = 80 };

    public SlaTargetOptions Medium { get; set; } = new() { AckMinutes = 240, ResolveMinutes = 4_320, EscalateAtPercent = 80 };

    public SlaTargetOptions Low { get; set; } = new() { AckMinutes = 480, ResolveMinutes = 7_200, EscalateAtPercent = 80 };

    public SlaTargetOptions For(Severity severity) => severity switch
    {
        Severity.Critical => Critical,
        Severity.High => High,
        Severity.Medium => Medium,
        Severity.Low => Low,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown severity."),
    };

    public SlaPolicy ToPolicy(Severity severity)
    {
        var target = For(severity);
        return new SlaPolicy(severity, target.AckMinutes, target.ResolveMinutes, target.EscalateAtPercent);
    }
}

public sealed class SlaTargetOptions
{
    public int AckMinutes { get; set; }

    public int ResolveMinutes { get; set; }

    public int EscalateAtPercent { get; set; }
}
