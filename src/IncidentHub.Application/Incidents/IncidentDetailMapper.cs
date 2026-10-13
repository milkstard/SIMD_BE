using IncidentHub.Application.Common;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;

namespace IncidentHub.Application.Incidents;

internal static class IncidentDetailMapper
{
    /// <summary>
    /// Builds the detail DTO for a freshly reported incident from the saved aggregate and the refs the caller already holds
    /// (a new incident has no assignee or team). Comments and attachments are empty until BE-08.
    /// </summary>
    public static IncidentDetailDto ToDetail(Incident incident, Actor actor, ApplicationRefDto application, UserRefDto reporter)
    {
        return new IncidentDetailDto(
            incident.Id,
            new IncidentNumber(incident.Number).ToString(),
            incident.Title,
            application,
            incident.Environment,
            incident.Severity,
            incident.Priority,
            incident.Status,
            Assignee: null,
            AssignedTeam: null,
            reporter,
            incident.CreatedAt,
            incident.UpdatedAt,
            incident.AckDueAt,
            incident.ResolveDueAt,
            incident.IsSlaBreached,
            CommentCount: 0,
            Convert.ToBase64String(incident.RowVersion),
            incident.Description,
            incident.StepsToReproduce,
            incident.Source,
            DuplicateOf: null,
            incident.CloseReason,
            incident.AcknowledgedAt,
            incident.ResolvedAt,
            incident.ClosedAt,
            Attachments: [],
            IncidentWorkflow.AllowedTransitions(incident, actor),
            CanEdit: IncidentPermissions.CanEdit(incident.Status, incident.ReporterId, actor),
            CanAssign: IncidentPermissions.CanAssign(incident.Status, actor));
    }
}
