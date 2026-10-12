using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;

namespace IncidentHub.Application.Abstractions;

/// <summary>Supplies the SLA targets for a severity. Config-backed until BE-12 reads the <c>SlaPolicies</c> table.</summary>
public interface ISlaPolicyProvider
{
    Task<SlaPolicy> GetAsync(Severity severity, CancellationToken cancellationToken);
}
