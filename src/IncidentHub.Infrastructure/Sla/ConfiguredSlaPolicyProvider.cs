using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Sla;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;
using Microsoft.Extensions.Options;

namespace IncidentHub.Infrastructure.Sla;

/// <summary>
/// Reads the policy from <c>Sla:Defaults</c> (with built-in fallbacks). Uses <see cref="IOptions{TOptions}"/> so the values
/// validated at host start are the ones used; a config change needs a restart. BE-12 replaces it with the <c>SlaPolicies</c> table.
/// </summary>
public sealed class ConfiguredSlaPolicyProvider(IOptions<SlaDefaultsOptions> options) : ISlaPolicyProvider
{
    public Task<SlaPolicy> GetAsync(Severity severity, CancellationToken cancellationToken) =>
        Task.FromResult(options.Value.ToPolicy(severity));
}
