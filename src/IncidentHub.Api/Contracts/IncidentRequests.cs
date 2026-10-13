using IncidentHub.Application.Common.Paging;
using IncidentHub.Application.Incidents.ListIncidents;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using Microsoft.AspNetCore.Mvc;

namespace IncidentHub.Api.Contracts;

/// <summary>Nullable members let a missing field reach the validator (422 with a field key) instead of a model-binding 400.</summary>
public sealed record ReportIncidentRequest(
    Guid? ApplicationId,
    AppEnvironment? Environment,
    Severity? Severity,
    string? Title,
    string? Description,
    string? StepsToReproduce,
    List<Guid>? AttachmentIds);

/// <summary>Body of <c>POST /incidents/{id}/transitions</c>. A missing <c>to</c> reaches the validator (422) instead of a model-binding 400.</summary>
public sealed record TransitionRequest(
    IncidentStatus? To,
    string? Note,
    CloseReason? CloseReason,
    Guid? DuplicateOfId);

/// <summary>Body of <c>POST /incidents/{id}/assignment</c>. Replaces both fields: <c>null</c> clears one.</summary>
public sealed record AssignRequest(Guid? AssigneeId, Guid? TeamId);

/// <summary>Query string of <c>GET /incidents</c>. Repeatable <c>status</c>/<c>severity</c>; unknown enum names or sort tokens fail binding (400).</summary>
public sealed class ListIncidentsRequest
{
    [FromQuery(Name = "status")]
    public IncidentStatus[]? Status { get; init; }

    [FromQuery(Name = "severity")]
    public Severity[]? Severity { get; init; }

    [FromQuery(Name = "applicationId")]
    public Guid? ApplicationId { get; init; }

    [FromQuery(Name = "environment")]
    public AppEnvironment? Environment { get; init; }

    [FromQuery(Name = "assigneeId")]
    public string? AssigneeId { get; init; }

    [FromQuery(Name = "teamId")]
    public Guid? TeamId { get; init; }

    [FromQuery(Name = "reporterId")]
    public string? ReporterId { get; init; }

    [FromQuery(Name = "slaBreached")]
    public bool? SlaBreached { get; init; }

    [FromQuery(Name = "q")]
    public string? Q { get; init; }

    [FromQuery(Name = "createdFrom")]
    public DateTimeOffset? CreatedFrom { get; init; }

    [FromQuery(Name = "createdTo")]
    public DateTimeOffset? CreatedTo { get; init; }

    /// <summary><c>createdAt</c>, <c>-createdAt</c> (default), <c>severity</c>, <c>-updatedAt</c>.</summary>
    [FromQuery(Name = "sort")]
    public string? Sort { get; init; }

    [FromQuery(Name = "cursor")]
    public string? Cursor { get; init; }

    [FromQuery(Name = "limit")]
    public int Limit { get; init; } = PageLimits.Default;

    [FromQuery(Name = "includeTotal")]
    public bool IncludeTotal { get; init; }
}

/// <summary>API-CONTRACT <c>AddCommentRequest</c>. A missing <c>isInternal</c> means a public comment.</summary>
public sealed record AddCommentRequest(string? Body, bool? IsInternal, List<Guid>? AttachmentIds);
