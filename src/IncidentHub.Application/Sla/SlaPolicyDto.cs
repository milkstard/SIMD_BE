using IncidentHub.Application.Common;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;

namespace IncidentHub.Application.Sla;

/// <param name="Id">The severity name; a policy is keyed by severity.</param>
public sealed record SlaPolicyDto(
    string Id,
    Severity Severity,
    int AckMinutes,
    int ResolveMinutes,
    int EscalateAtPercent,
    string RowVersion) : IVersionedDto
{
    public static SlaPolicyDto From(SlaPolicySetting setting) =>
        new(
            setting.Severity.ToString(),
            setting.Severity,
            setting.AckMinutes,
            setting.ResolveMinutes,
            setting.EscalateAtPercent,
            Convert.ToBase64String(setting.RowVersion));
}
