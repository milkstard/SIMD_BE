using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Incidents;

internal static class IncidentProjections
{
    /// <summary>
    /// Projects incidents straight to <see cref="IncidentSummaryDto"/> rows without loading aggregates. <see cref="Incident"/> has no
    /// navigations, so refs are joined by key; filter <paramref name="incidents"/> before passing it in. Number and RowVersion are formatted by <see cref="ToSummary"/> after materialising.
    /// </summary>
    public static IQueryable<SummaryRow> SummaryRows(IAppDbContext db, IQueryable<Incident> incidents) =>
        from i in incidents.AsNoTracking()
        join a in db.Applications.AsNoTracking() on i.ApplicationId equals a.Id
        join r in db.Users.AsNoTracking() on i.ReporterId equals r.Id
        join u in db.Users.AsNoTracking() on i.AssigneeId equals u.Id into assignees
        from assignee in assignees.DefaultIfEmpty()
        join t in db.Teams.AsNoTracking() on i.AssignedTeamId equals t.Id into teams
        from team in teams.DefaultIfEmpty()
        select new SummaryRow(
            i.Id,
            i.Number,
            i.Title,
            new ApplicationRefDto(a.Id, a.Name, a.Code),
            i.Environment,
            i.Severity,
            i.Priority,
            i.Status,
            assignee == null ? null : new UserRefDto(assignee.Id, assignee.DisplayName, assignee.Email),
            team == null ? null : new TeamRefDto(team.Id, team.Name),
            new UserRefDto(r.Id, r.DisplayName, r.Email),
            i.CreatedAt,
            i.UpdatedAt,
            i.AckDueAt,
            i.ResolveDueAt,
            i.IsSlaBreached,
            i.RowVersion);

    // Comments arrive with BE-08, so the count is 0 until then.
    public static IncidentSummaryDto ToSummary(SummaryRow row) =>
        new(
            row.Id,
            new IncidentNumber(row.Number).ToString(),
            row.Title,
            row.Application,
            row.Environment,
            row.Severity,
            row.Priority,
            row.Status,
            row.Assignee,
            row.AssignedTeam,
            row.Reporter,
            row.CreatedAt,
            row.UpdatedAt,
            row.AckDueAt,
            row.ResolveDueAt,
            row.IsSlaBreached,
            CommentCount: 0,
            Convert.ToBase64String(row.RowVersion));

    public sealed record SummaryRow(
        Guid Id,
        int Number,
        string Title,
        ApplicationRefDto Application,
        AppEnvironment Environment,
        Severity Severity,
        Priority? Priority,
        IncidentStatus Status,
        UserRefDto? Assignee,
        TeamRefDto? AssignedTeam,
        UserRefDto Reporter,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? AckDueAt,
        DateTimeOffset? ResolveDueAt,
        bool IsSlaBreached,
        byte[] RowVersion);
}
