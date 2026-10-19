using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Sla;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IncidentHub.Infrastructure.Sla;

/// <summary>
/// Reads the policy of a severity from the <c>SlaPolicies</c> table: a primary-key read of a four-row table, once per
/// report or severity change, so it is not cached. A missing row falls back to <c>Sla:Defaults</c> and logs a warning
/// rather than failing the report.
/// </summary>
public sealed class DbSlaPolicyProvider(
    IAppDbContext db,
    IOptions<SlaDefaultsOptions> defaults,
    ILogger<DbSlaPolicyProvider> logger) : ISlaPolicyProvider
{
    public async Task<SlaPolicy> GetAsync(Severity severity, CancellationToken cancellationToken)
    {
        var setting = await db.SlaPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Severity == severity, cancellationToken);

        if (setting is not null)
        {
            return setting.ToPolicy();
        }

        logger.LogWarning("SLA policy {Severity} is missing from the SlaPolicies table; using Sla:Defaults", severity);
        return defaults.Value.ToPolicy(severity);
    }
}
