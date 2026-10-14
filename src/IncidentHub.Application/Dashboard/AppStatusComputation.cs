using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common;
using IncidentHub.Application.Dashboard.Dtos;
using IncidentHub.Domain.Apps;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Dashboard;

/// <summary>A derived status together with the team whose dashboard shows it.</summary>
public sealed record ComputedAppStatus(Guid OwningTeamId, AppStatusItemDto Item);

public static class AppStatusComputation
{
    /// <summary>Computes the status of the active applications among <paramref name="applicationIds"/> from SQL; unknown or inactive ones are absent.</summary>
    public static async Task<IReadOnlyDictionary<Guid, ComputedAppStatus>> ComputeAsync(
        IAppDbContext db,
        IReadOnlyCollection<Guid> applicationIds,
        CancellationToken cancellationToken)
    {
        if (applicationIds.Count == 0)
        {
            return new Dictionary<Guid, ComputedAppStatus>();
        }

        var apps = await db.Applications.AsNoTracking()
            .Where(a => a.IsActive && applicationIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name, a.Code, a.OwningTeamId })
            .ToListAsync(cancellationToken);

        var facts = await OpenIncidentFacts.LoadAsync(db, apps.Select(a => a.Id).ToList(), cancellationToken);

        return apps.ToDictionary(
            a => a.Id,
            a =>
            {
                var result = AppStatusCalculator.Calculate(facts.TryGetValue(a.Id, out var list) ? list : []);
                return new ComputedAppStatus(
                    a.OwningTeamId,
                    new AppStatusItemDto(
                        new ApplicationRefDto(a.Id, a.Name, a.Code),
                        result.Status,
                        result.OpenIncidents,
                        result.WorstOpenSeverity,
                        result.Since));
            });
    }
}
