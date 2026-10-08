using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Paging;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Teams.ListTeams;

public sealed class ListTeamsHandler(IAppDbContext db) : IRequestHandler<ListTeamsQuery, Paged<TeamDto>>
{
    public async Task<Paged<TeamDto>> Handle(ListTeamsQuery request, CancellationToken cancellationToken)
    {
        var cursor = KeysetCursor.DecodeOrThrow(request.Cursor);

        var query = db.Teams.AsNoTracking();
        var total = request.IncludeTotal ? await query.CountAsync(cancellationToken) : (int?)null;

        var ordered = query.OrderBy(t => t.Name).ThenBy(t => t.Id);
        var filtered = cursor is { } c
            ? ordered.Where(t => t.Name.CompareTo(c.Name) > 0 || (t.Name == c.Name && t.Id.CompareTo(c.Id) > 0))
            : ordered;

        var rows = await filtered.Take(request.Limit + 1).Select(TeamDto.Projection).ToListAsync(cancellationToken);

        string? next = null;
        if (rows.Count > request.Limit)
        {
            rows.RemoveAt(rows.Count - 1);
            var last = rows[^1];
            next = KeysetCursor.Encode(last.Name, last.Id);
        }

        return new Paged<TeamDto>(rows, next, total);
    }
}
