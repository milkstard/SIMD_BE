using IncidentHub.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Incidents.ListHistory;

/// <summary>Turns the stored ids of id-valued history fields into display names, one lookup per kind per page.</summary>
internal static class HistoryValueResolver
{
    public sealed record RawEntry(string Field, string? OldValue, string? NewValue);

    /// <returns>For each (field, raw value) the text to show; a value whose row is gone stays as it was stored.</returns>
    public static async Task<IReadOnlyDictionary<(string Field, string Value), string>> ResolveAsync(
        IAppDbContext db,
        IReadOnlyCollection<RawEntry> entries,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<(string, string), string>();

        var userIds = IdsOf(entries, HistoryFieldNames.Assignee);
        if (userIds.Count > 0)
        {
            var names = await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName })
                .ToListAsync(cancellationToken);
            Add(result, HistoryFieldNames.Assignee, names.Select(n => (n.Id, n.DisplayName)));
        }

        var teamIds = IdsOf(entries, HistoryFieldNames.Team);
        if (teamIds.Count > 0)
        {
            var names = await db.Teams.AsNoTracking()
                .Where(t => teamIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Name })
                .ToListAsync(cancellationToken);
            Add(result, HistoryFieldNames.Team, names.Select(n => (n.Id, n.Name)));
        }

        var appIds = IdsOf(entries, HistoryFieldNames.Application);
        if (appIds.Count > 0)
        {
            var names = await db.Applications.AsNoTracking()
                .Where(a => appIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Name })
                .ToListAsync(cancellationToken);
            Add(result, HistoryFieldNames.Application, names.Select(n => (n.Id, n.Name)));
        }

        var incidentIds = IdsOf(entries, HistoryFieldNames.DuplicateOf);
        if (incidentIds.Count > 0)
        {
            var numbers = await db.Incidents.AsNoTracking()
                .Where(i => incidentIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Number })
                .ToListAsync(cancellationToken);
            Add(result, HistoryFieldNames.DuplicateOf, numbers.Select(n => (n.Id, new IncidentHub.Domain.Incidents.IncidentNumber(n.Number).ToString())));
        }

        return result;
    }

    private static List<Guid> IdsOf(IReadOnlyCollection<RawEntry> entries, string field) =>
        entries.Where(e => e.Field == field)
            .SelectMany(e => new[] { e.OldValue, e.NewValue })
            .Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

    private static void Add(Dictionary<(string, string), string> target, string field, IEnumerable<(Guid Id, string Name)> rows)
    {
        foreach (var (id, name) in rows)
        {
            target[(field, id.ToString("D"))] = name;
        }
    }
}
