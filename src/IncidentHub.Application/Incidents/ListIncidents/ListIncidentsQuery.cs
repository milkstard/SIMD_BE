using IncidentHub.Application.Common.Paging;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using MediatR;

namespace IncidentHub.Application.Incidents.ListIncidents;

/// <param name="AssigneeId">A GUID, <c>me</c> or <c>unassigned</c>.</param>
/// <param name="TeamId">Filters the assigned team, not the application's owning team.</param>
/// <param name="ReporterId">A GUID or <c>me</c>.</param>
public sealed record ListIncidentsQuery(
    IReadOnlyList<IncidentStatus> Status,
    IReadOnlyList<Severity> Severity,
    Guid? ApplicationId,
    AppEnvironment? Environment,
    string? AssigneeId,
    Guid? TeamId,
    string? ReporterId,
    bool? SlaBreached,
    string? Q,
    DateTimeOffset? CreatedFrom,
    DateTimeOffset? CreatedTo,
    IncidentSort Sort = IncidentSort.CreatedAtDesc,
    string? Cursor = null,
    int Limit = PageLimits.Default,
    bool IncludeTotal = false) : IRequest<Paged<IncidentSummaryDto>>
{
    public const string Me = "me";
    public const string Unassigned = "unassigned";
    public const int MaxQueryLength = 200;
}
