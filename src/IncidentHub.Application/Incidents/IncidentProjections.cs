using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
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

    /// <summary>The detail-only columns of one incident (the summary columns come from <see cref="SummaryRows"/>).</summary>
    public static IQueryable<DetailExtrasRow> DetailExtras(IAppDbContext db, Guid incidentId) =>
        from i in db.Incidents.AsNoTracking()
        where i.Id == incidentId
        join d in db.Incidents.AsNoTracking() on i.DuplicateOfId equals d.Id into duplicates
        from duplicate in duplicates.DefaultIfEmpty()
        select new DetailExtrasRow(
            i.Description,
            i.StepsToReproduce,
            i.Source,
            i.CloseReason,
            i.AcknowledgedAt,
            i.ResolvedAt,
            i.ClosedAt,
            duplicate == null ? null : duplicate.Id,
            duplicate == null ? null : (int?)duplicate.Number);

    // Comments and attachments arrive with BE-08: count 0 and no attachments until then.
    public static IncidentDetailDto ToDetail(SummaryRow row, DetailExtrasRow extras, Actor actor)
    {
        var summary = ToSummary(row);
        return new IncidentDetailDto(
            summary.Id,
            summary.Number,
            summary.Title,
            summary.Application,
            summary.Environment,
            summary.Severity,
            summary.Priority,
            summary.Status,
            summary.Assignee,
            summary.AssignedTeam,
            summary.Reporter,
            summary.CreatedAt,
            summary.UpdatedAt,
            summary.AckDueAt,
            summary.ResolveDueAt,
            summary.IsSlaBreached,
            summary.CommentCount,
            summary.RowVersion,
            extras.Description,
            extras.StepsToReproduce,
            extras.Source,
            extras is { DuplicateOfId: { } duplicateId, DuplicateOfNumber: { } duplicateNumber }
                ? new DuplicateOfDto(duplicateId, new IncidentNumber(duplicateNumber).ToString())
                : null,
            extras.CloseReason,
            extras.AcknowledgedAt,
            extras.ResolvedAt,
            extras.ClosedAt,
            Attachments: [],
            IncidentPermissions.AllowedTransitions(row.Status, row.Reporter.Id, row.Assignee?.Id, actor),
            IncidentPermissions.CanEdit(row.Status, row.Reporter.Id, actor),
            IncidentPermissions.CanAssign(row.Status, actor));
    }

    public sealed record DetailExtrasRow(
        string Description,
        string? StepsToReproduce,
        IncidentSource Source,
        CloseReason? CloseReason,
        DateTimeOffset? AcknowledgedAt,
        DateTimeOffset? ResolvedAt,
        DateTimeOffset? ClosedAt,
        Guid? DuplicateOfId,
        int? DuplicateOfNumber);

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
