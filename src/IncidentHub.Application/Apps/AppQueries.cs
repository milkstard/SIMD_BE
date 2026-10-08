using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Users;

namespace IncidentHub.Application.Apps;

internal static class AppQueries
{
    /// <summary>Projects applications with their owning team and escalation contact straight to DTOs.</summary>
    public static IQueryable<ApplicationDto> Project(IAppDbContext db, IQueryable<MonitoredApp> apps) =>
        from a in apps
        join t in db.Teams on a.OwningTeamId equals t.Id
        join u in db.Users on a.EscalationUserId equals u.Id into users
        from u in users.DefaultIfEmpty()
        select new ApplicationDto(
            a.Id,
            a.Name,
            a.Code,
            new TeamRefDto(t.Id, t.Name),
            u == null ? null : new UserRefDto(u.Id, u.DisplayName, u.Email),
            a.Environments,
            a.IsActive,
            Convert.ToBase64String(a.RowVersion));

    /// <summary>Admin sees every application; everyone else only those owned by a team they belong to.</summary>
    public static IQueryable<MonitoredApp> VisibleTo(IQueryable<MonitoredApp> apps, IAppDbContext db, ICurrentUser user)
    {
        if (user.IsInRole(UserRole.Admin))
        {
            return apps;
        }

        var userId = user.UserId;
        return apps.Where(a => db.TeamMembers.Any(m => m.TeamId == a.OwningTeamId && m.UserId == userId));
    }
}
