using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Paging;
using IncidentHub.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Apps.ListApps;

public sealed class ListAppsHandler(IAppDbContext db, ICurrentUser user)
    : IRequestHandler<ListAppsQuery, Paged<ApplicationDto>>
{
    public async Task<Paged<ApplicationDto>> Handle(ListAppsQuery request, CancellationToken cancellationToken)
    {
        var cursor = KeysetCursor.DecodeOrThrow(request.Cursor);

        // Only Admin may ask for inactive applications; for everyone else the flag is ignored.
        var includeInactive = request.IncludeInactive && user.IsInRole(UserRole.Admin);

        var visible = AppQueries.VisibleTo(db.Applications.AsNoTracking(), db, user);
        if (!includeInactive)
        {
            visible = visible.Where(a => a.IsActive);
        }

        var ordered = AppQueries.Project(db, visible).OrderBy(a => a.Name).ThenBy(a => a.Id);
        var filtered = cursor is { } c
            ? ordered.Where(a => a.Name.CompareTo(c.Name) > 0 || (a.Name == c.Name && a.Id.CompareTo(c.Id) > 0))
            : ordered;

        var rows = await filtered.Take(request.Limit + 1).ToListAsync(cancellationToken);

        string? next = null;
        if (rows.Count > request.Limit)
        {
            rows.RemoveAt(rows.Count - 1);
            var last = rows[^1];
            next = KeysetCursor.Encode(last.Name, last.Id);
        }

        return new Paged<ApplicationDto>(rows, next, null);
    }
}
