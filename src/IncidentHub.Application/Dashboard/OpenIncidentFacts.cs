using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Dashboard;

/// <summary>The one definition of an "open" incident for the whole dashboard: every status except Resolved and Closed (Reopened counts).</summary>
public static class OpenIncidentFacts
{
    public static IQueryable<Incident> Open(IQueryable<Incident> incidents) =>
        incidents.Where(i => i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.Closed);

    /// <summary>Open incidents of the given applications as calculator input, grouped by application.</summary>
    public static async Task<IReadOnlyDictionary<Guid, List<OpenIncidentFact>>> LoadAsync(
        IAppDbContext db,
        IReadOnlyCollection<Guid> applicationIds,
        CancellationToken cancellationToken)
    {
        var rows = await Open(db.Incidents.AsNoTracking())
            .Where(i => applicationIds.Contains(i.ApplicationId))
            .Select(i => new { i.ApplicationId, i.Severity, i.Environment, i.CreatedAt })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ApplicationId)
            .ToDictionary(g => g.Key, g => g.Select(r => new OpenIncidentFact(r.Severity, r.Environment, r.CreatedAt)).ToList());
    }
}
