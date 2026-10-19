using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;

namespace IncidentHub.Application.Abstractions;

/// <summary>Supplies the SLA targets for a severity. Reads the <c>SlaPolicies</c> table (falling back to <c>Sla:Defaults</c> when a row is missing).</summary>
public interface ISlaPolicyProvider
{
    Task<SlaPolicy> GetAsync(Severity severity, CancellationToken cancellationToken);
}
