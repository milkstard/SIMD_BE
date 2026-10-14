using System.Globalization;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common;
using IncidentHub.Application.Dashboard.Dtos;
using IncidentHub.Domain.Incidents;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Dashboard;

/// <summary>The summary queries. Everything is read with <c>AsNoTracking()</c> and projected to numbers; nothing loads an aggregate.</summary>
public static class DashboardSummaryQueries
{
    public static async Task<DashboardSummaryDto> ComputeAsync(
        IAppDbContext db,
        IReadOnlyCollection<Guid> teamIds,
        DashboardRange range,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var start = range.WindowStart(time);

        if (teamIds.Count == 0)
        {
            return Build(range, now, start, [], new Dictionary<Guid, ApplicationRefDto>(), null, null, new Dictionary<DateTime, int>(), new Dictionary<DateTime, int>());
        }

        var appIds = db.Applications.AsNoTracking().Where(a => teamIds.Contains(a.OwningTeamId)).Select(a => a.Id);
        var incidents = db.Incidents.AsNoTracking().Where(i => appIds.Contains(i.ApplicationId));

        // Current state, not range-limited: one grouped query feeds the totals, the severity split and the per-application list.
        var groups = await OpenIncidentFacts.Open(incidents)
            .GroupBy(i => new { i.ApplicationId, i.Severity, Unassigned = i.AssigneeId == null, i.IsSlaBreached })
            .Select(g => new OpenGroup(g.Key.ApplicationId, g.Key.Severity, g.Key.Unassigned, g.Key.IsSlaBreached, g.Count()))
            .ToListAsync(cancellationToken);

        var openAppIds = groups.Select(g => g.ApplicationId).Distinct().ToList();
        var apps = openAppIds.Count == 0
            ? new Dictionary<Guid, ApplicationRefDto>()
            : await db.Applications.AsNoTracking()
                .Where(a => openAppIds.Contains(a.Id))
                .Select(a => new ApplicationRefDto(a.Id, a.Name, a.Code))
                .ToDictionaryAsync(a => a.Id, cancellationToken);

        // Two timestamp columns per qualifying incident, averaged here: provider functions (DATEDIFF) stay out of Application (ADR 0001).
        var acknowledged = await incidents
            .Where(i => i.AcknowledgedAt != null && i.AcknowledgedAt >= start)
            .Select(i => new { i.CreatedAt, At = i.AcknowledgedAt!.Value })
            .ToListAsync(cancellationToken);
        var resolvedTimes = await incidents
            .Where(i => i.ResolvedAt != null && i.ResolvedAt >= start)
            .Select(i => new { i.CreatedAt, At = i.ResolvedAt!.Value })
            .ToListAsync(cancellationToken);
        var mtta = acknowledged.Count == 0 ? (double?)null : acknowledged.Average(r => (r.At - r.CreatedAt).TotalSeconds);
        var mttr = resolvedTimes.Count == 0 ? (double?)null : resolvedTimes.Average(r => (r.At - r.CreatedAt).TotalSeconds);

        // Times are stored in UTC, so the date part is the UTC day.
        var reported = await incidents
            .Where(i => i.CreatedAt >= start)
            .GroupBy(i => i.CreatedAt.Date)
            .Select(g => new DayCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken);
        var resolved = await incidents
            .Where(i => i.ResolvedAt != null && i.ResolvedAt >= start)
            .GroupBy(i => i.ResolvedAt!.Value.Date)
            .Select(g => new DayCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        return Build(
            range,
            now,
            start,
            groups,
            apps,
            mtta,
            mttr,
            reported.ToDictionary(r => r.Day, r => r.Count),
            resolved.ToDictionary(r => r.Day, r => r.Count));
    }

    private static DashboardSummaryDto Build(
        DashboardRange range,
        DateTimeOffset now,
        DateTimeOffset start,
        IReadOnlyList<OpenGroup> groups,
        IReadOnlyDictionary<Guid, ApplicationRefDto> apps,
        double? mttaSeconds,
        double? mttrSeconds,
        IReadOnlyDictionary<DateTime, int> reported,
        IReadOnlyDictionary<DateTime, int> resolved)
    {
        var bySeverity = Enum.GetValues<Severity>().ToDictionary(s => s, s => groups.Where(g => g.Severity == s).Sum(g => g.Count));

        var byApplication = groups
            .GroupBy(g => g.ApplicationId)
            .Where(g => apps.ContainsKey(g.Key))
            .Select(g => new OpenByApplicationDto(
                apps[g.Key],
                g.Sum(x => x.Count),
                g.Where(x => x.Severity == Severity.Critical).Sum(x => x.Count)))
            .OrderByDescending(a => a.Open).ThenBy(a => a.Application.Name, StringComparer.Ordinal).ThenBy(a => a.Application.Id)
            .ToList();

        var firstDay = start.UtcDateTime.Date;
        var trend = Enumerable.Range(0, range.Days())
            .Select(offset => firstDay.AddDays(offset))
            .Select(day => new TrendPointDto(
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                reported.GetValueOrDefault(day),
                resolved.GetValueOrDefault(day)))
            .ToList();

        return new DashboardSummaryDto(
            range.Token(),
            now,
            groups.Sum(g => g.Count),
            groups.Where(g => g.Unassigned).Sum(g => g.Count),
            groups.Where(g => g.IsSlaBreached).Sum(g => g.Count),
            bySeverity,
            byApplication,
            Minutes(mttaSeconds),
            Minutes(mttrSeconds),
            trend);
    }

    private static double? Minutes(double? seconds) =>
        seconds is null ? null : Math.Round(seconds.Value / 60d, 1, MidpointRounding.AwayFromZero);

    private sealed record OpenGroup(Guid ApplicationId, Severity Severity, bool Unassigned, bool IsSlaBreached, int Count);

    private sealed record DayCount(DateTime Day, int Count);
}
