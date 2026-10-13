using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Paging;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Incidents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Incidents.ListIncidents;

public sealed class ListIncidentsHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IAppTeamReader teams,
    IIncidentTextSearch search) : IRequestHandler<ListIncidentsQuery, Paged<IncidentSummaryDto>>
{
    /// <summary>Most ranked ids a text search hands to the list filters.</summary>
    public const int TextSearchLimit = 500;

    public async Task<Paged<IncidentSummaryDto>> Handle(ListIncidentsQuery request, CancellationToken cancellationToken)
    {
        var cursor = IncidentCursor.DecodeOrThrow(request.Cursor, request.Sort);

        // Visibility: only applications of the caller's teams (Admin included). A foreign applicationId simply matches nothing.
        var appIds = (await teams.GetApplicationIdsForUserAsync(currentUser.UserId, cancellationToken)).ToArray();
        if (appIds.Length == 0 || (request.ApplicationId is { } requested && !appIds.Contains(requested)))
        {
            return new Paged<IncidentSummaryDto>([], null, request.IncludeTotal ? 0 : null);
        }

        var filtered = ApplyFilters(db.Incidents.AsNoTracking().Where(i => appIds.Contains(i.ApplicationId)), request);

        var text = request.Q?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var ids = (await search.FindIdsAsync(text, appIds, TextSearchLimit, cancellationToken)).ToArray();
            if (ids.Length == 0)
            {
                return new Paged<IncidentSummaryDto>([], null, request.IncludeTotal ? 0 : null);
            }

            filtered = filtered.Where(i => ids.Contains(i.Id));
        }

        var total = request.IncludeTotal ? await filtered.CountAsync(cancellationToken) : (int?)null;

        // Order and page on the bare incidents (index friendly), then project just that page; the projection joins give no order.
        var pageIds = await Order(ApplyCursor(filtered, request.Sort, cursor), request.Sort)
            .Take(request.Limit + 1)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        var idArray = pageIds.ToArray();
        var projected = await IncidentProjections
            .SummaryRows(db, db.Incidents.Where(i => idArray.Contains(i.Id)))
            .ToListAsync(cancellationToken);

        var position = pageIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        var rows = projected.OrderBy(r => position[r.Id]).ToList();

        string? next = null;
        if (rows.Count > request.Limit)
        {
            rows.RemoveAt(rows.Count - 1);
            next = ToCursor(request.Sort, rows[^1]).Encode();
        }

        return new Paged<IncidentSummaryDto>(rows.Select(IncidentProjections.ToSummary).ToList(), next, total);
    }

    private IQueryable<Incident> ApplyFilters(IQueryable<Incident> query, ListIncidentsQuery request)
    {
        if (request.Status.Count > 0)
        {
            // Inlined as literals (the sets are tiny enums): a parameterised list hides the selectivity and the optimizer scans the table.
            var statuses = request.Status.Distinct().ToArray();
            query = query.Where(i => EF.Constant(statuses).Contains(i.Status));
        }

        if (request.Severity.Count > 0)
        {
            var severities = request.Severity.Distinct().ToArray();
            query = query.Where(i => EF.Constant(severities).Contains(i.Severity));
        }

        if (request.ApplicationId is { } applicationId)
        {
            query = query.Where(i => i.ApplicationId == applicationId);
        }

        if (request.Environment is { } environment)
        {
            query = query.Where(i => i.Environment == environment);
        }

        if (request.AssigneeId is { } assignee)
        {
            if (assignee.Equals(ListIncidentsQuery.Unassigned, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(i => i.AssigneeId == null);
            }
            else
            {
                var assigneeId = ResolveUser(assignee);
                query = query.Where(i => i.AssigneeId == assigneeId);
            }
        }

        if (request.TeamId is { } teamId)
        {
            query = query.Where(i => i.AssignedTeamId == teamId);
        }

        if (request.ReporterId is { } reporter)
        {
            var reporterId = ResolveUser(reporter);
            query = query.Where(i => i.ReporterId == reporterId);
        }

        if (request.SlaBreached is { } breached)
        {
            query = query.Where(i => i.IsSlaBreached == breached);
        }

        if (request.CreatedFrom is { } from)
        {
            query = query.Where(i => i.CreatedAt >= from);
        }

        if (request.CreatedTo is { } to)
        {
            query = query.Where(i => i.CreatedAt <= to);
        }

        return query;
    }

    private Guid ResolveUser(string token) =>
        token.Equals(ListIncidentsQuery.Me, StringComparison.OrdinalIgnoreCase) ? currentUser.UserId : Guid.Parse(token);

    private static IQueryable<Incident> ApplyCursor(IQueryable<Incident> query, IncidentSort sort, IncidentCursor? cursor)
    {
        if (cursor is null)
        {
            return query;
        }

        var time = cursor.Time;
        var id = cursor.Id;
        var rank = cursor.Rank;

        return sort switch
        {
            IncidentSort.CreatedAtAsc => query.Where(i =>
                i.CreatedAt > time || (i.CreatedAt == time && i.Id.CompareTo(id) > 0)),
            IncidentSort.UpdatedAtDesc => query.Where(i =>
                i.UpdatedAt < time || (i.UpdatedAt == time && i.Id.CompareTo(id) < 0)),
            IncidentSort.Severity => query.Where(i =>
                (i.Severity == Severity.Critical ? 0 : i.Severity == Severity.High ? 1 : i.Severity == Severity.Medium ? 2 : 3) > rank
                || ((i.Severity == Severity.Critical ? 0 : i.Severity == Severity.High ? 1 : i.Severity == Severity.Medium ? 2 : 3) == rank
                    && (i.CreatedAt < time || (i.CreatedAt == time && i.Id.CompareTo(id) < 0)))),
            _ => query.Where(i =>
                i.CreatedAt < time || (i.CreatedAt == time && i.Id.CompareTo(id) < 0)),
        };
    }

    private static IQueryable<Incident> Order(IQueryable<Incident> query, IncidentSort sort) =>
        sort switch
        {
            IncidentSort.CreatedAtAsc => query.OrderBy(i => i.CreatedAt).ThenBy(i => i.Id),
            IncidentSort.UpdatedAtDesc => query.OrderByDescending(i => i.UpdatedAt).ThenByDescending(i => i.Id),
            IncidentSort.Severity => query
                .OrderBy(i => i.Severity == Severity.Critical ? 0 : i.Severity == Severity.High ? 1 : i.Severity == Severity.Medium ? 2 : 3)
                .ThenByDescending(i => i.CreatedAt)
                .ThenByDescending(i => i.Id),
            _ => query.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id),
        };

    private static IncidentCursor ToCursor(IncidentSort sort, IncidentProjections.SummaryRow last) =>
        sort switch
        {
            IncidentSort.UpdatedAtDesc => new IncidentCursor(sort, last.UpdatedAt, 0, last.Id),
            IncidentSort.Severity => new IncidentCursor(sort, last.CreatedAt, SeverityRank(last.Severity), last.Id),
            _ => new IncidentCursor(sort, last.CreatedAt, 0, last.Id),
        };

    private static int SeverityRank(Severity severity) =>
        severity switch
        {
            Severity.Critical => 0,
            Severity.High => 1,
            Severity.Medium => 2,
            _ => 3,
        };
}
