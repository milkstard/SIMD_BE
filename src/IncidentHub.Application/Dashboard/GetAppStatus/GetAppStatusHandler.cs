using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common;
using IncidentHub.Application.Dashboard.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Dashboard.GetAppStatus;

/// <summary>One item per active application owned by a team of the caller (Admin included), ordered by name.</summary>
public sealed class GetAppStatusHandler(IAppDbContext db, IAppTeamReader teams, IAppStatusReader statuses, ICurrentUser user)
    : IRequestHandler<GetAppStatusQuery, IReadOnlyList<AppStatusItemDto>>
{
    public async Task<IReadOnlyList<AppStatusItemDto>> Handle(GetAppStatusQuery request, CancellationToken cancellationToken)
    {
        var visible = await teams.GetApplicationIdsForUserAsync(user.UserId, cancellationToken);
        if (visible.Count == 0)
        {
            return [];
        }

        // Names come from SQL, not the cache, so a rename never shows stale; inactive applications drop out here.
        var apps = await db.Applications.AsNoTracking()
            .Where(a => a.IsActive && visible.Contains(a.Id))
            .OrderBy(a => a.Name).ThenBy(a => a.Id)
            .Select(a => new ApplicationRefDto(a.Id, a.Name, a.Code))
            .ToListAsync(cancellationToken);

        var items = (await statuses.GetAsync(apps.Select(a => a.Id).ToList(), cancellationToken)).ToDictionary(i => i.Application.Id);

        return apps
            .Where(a => items.ContainsKey(a.Id))
            .Select(a => items[a.Id] with { Application = a })
            .ToList();
    }
}
