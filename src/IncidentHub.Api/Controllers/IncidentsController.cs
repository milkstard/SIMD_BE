using IncidentHub.Api.Auth;
using IncidentHub.Api.Contracts;
using IncidentHub.Api.Filters;
using IncidentHub.Application.Common.Paging;
using IncidentHub.Application.Incidents.AddComment;
using IncidentHub.Application.Incidents.AssignIncident;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Application.Incidents.FindSimilarIncidents;
using IncidentHub.Application.Incidents.GetIncident;
using IncidentHub.Application.Incidents.ListComments;
using IncidentHub.Application.Incidents.ListHistory;
using IncidentHub.Application.Incidents.ListIncidents;
using IncidentHub.Application.Incidents.ReportIncident;
using IncidentHub.Application.Incidents.TransitionIncident;
using IncidentHub.Application.Incidents.UpdateIncident;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IncidentHub.Api.Controllers;

[ApiController]
[Route("api/v1/incidents")]
public sealed class IncidentsController(ISender sender) : ControllerBase
{
    // Description (20k) + steps (5k) characters plus JSON overhead, with headroom for multi-byte text.
    private const int MaxRequestBytes = 256 * 1024;

    // A comment body is at most 10,000 characters; JSON escaping of markup can multiply that several times.
    private const int MaxCommentRequestBytes = 128 * 1024;

    [HttpPost]
    [Authorize(Policy = Policies.CanReportIncident)]
    [Idempotent]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<IncidentDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IncidentDetailDto>> Report(
        [FromBody] ReportIncidentRequest request,
        CancellationToken cancellationToken)
    {
        // A missing enum maps to an undefined value so the validator answers 422 on that field.
        var incident = await sender.Send(
            new ReportIncidentCommand(
                request.ApplicationId ?? Guid.Empty,
                request.Environment ?? (AppEnvironment)(-1),
                request.Severity ?? (Severity)(-1),
                request.Title ?? string.Empty,
                request.Description ?? string.Empty,
                request.StepsToReproduce,
                request.AttachmentIds ?? []),
            cancellationToken);
        return Created($"/api/v1/incidents/{incident.Number}", incident);
    }

    [HttpGet("similar")]
    [Authorize(Policy = Policies.CanReportIncident)]
    [ProducesResponseType<IReadOnlyList<IncidentSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IReadOnlyList<IncidentSummaryDto>>> Similar(
        [FromQuery] Guid? applicationId,
        [FromQuery] string? title,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new FindSimilarIncidentsQuery(applicationId ?? Guid.Empty, title ?? string.Empty),
            cancellationToken));

    [HttpGet]
    [Authorize(Policy = Policies.CanViewIncident)]
    [ProducesResponseType<Paged<IncidentSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<Paged<IncidentSummaryDto>>> List(
        [FromQuery] ListIncidentsRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseSort(request.Sort, out var sort))
        {
            ModelState.AddModelError("sort", "Sort must be createdAt, -createdAt, severity or -updatedAt.");
            return ValidationProblem(ModelState);
        }

        return Ok(await sender.Send(
            new ListIncidentsQuery(
                request.Status ?? [],
                request.Severity ?? [],
                request.ApplicationId,
                request.Environment,
                request.AssigneeId,
                request.TeamId,
                request.ReporterId,
                request.SlaBreached,
                request.Q,
                request.CreatedFrom,
                request.CreatedTo,
                sort,
                request.Cursor,
                request.Limit,
                request.IncludeTotal),
            cancellationToken));
    }

    // "similar" is a literal segment and wins over this parameter route.
    [HttpGet("{number}")]
    [Authorize(Policy = Policies.CanViewIncident)]
    [ProducesResponseType<IncidentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IncidentDetailDto>> Get(string number, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetIncidentQuery(number), cancellationToken));

    [HttpPost("{id:guid}/transitions")]
    [Authorize(Policy = Policies.CanTransitionIncident)]
    [RequireIfMatch]
    [ProducesResponseType<IncidentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<IncidentDetailDto>> Transition(
        Guid id,
        [FromBody] TransitionRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new TransitionIncidentCommand(
                id,
                request.To ?? (IncidentStatus)(-1),
                request.Note,
                request.CloseReason,
                request.DuplicateOfId,
                HttpContext.GetIfMatchRowVersion()),
            cancellationToken));

    [HttpPost("{id:guid}/assignment")]
    [Authorize(Policy = Policies.CanAssignIncident)]
    [RequireIfMatch]
    [ProducesResponseType<IncidentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<IncidentDetailDto>> Assign(
        Guid id,
        [FromBody] AssignRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new AssignIncidentCommand(id, request.AssigneeId, request.TeamId, HttpContext.GetIfMatchRowVersion()),
            cancellationToken));

    // Responder-level fields are checked in the handler (the reporter may edit title, description and steps).
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = Policies.CanViewIncident)]
    [RequireIfMatch]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType<IncidentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<IncidentDetailDto>> Update(
        Guid id,
        [FromBody] UpdateIncidentRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(request.ToCommand(id, HttpContext.GetIfMatchRowVersion()), cancellationToken));

    [HttpGet("{id:guid}/comments")]
    [Authorize(Policy = Policies.CanViewIncident)]
    [ProducesResponseType<Paged<CommentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<Paged<CommentDto>>> ListComments(
        Guid id,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken,
        [FromQuery] int limit = PageLimits.Default,
        [FromQuery] bool includeTotal = false) =>
        Ok(await sender.Send(new ListCommentsQuery(id, cursor, limit, includeTotal), cancellationToken));

    // Whether an internal comment is allowed depends on the caller's role, so the handler decides (403), not the policy.
    [HttpPost("{id:guid}/comments")]
    [Authorize(Policy = Policies.CanViewIncident)]
    [Idempotent]
    [RequestSizeLimit(MaxCommentRequestBytes)]
    [ProducesResponseType<CommentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CommentDto>> AddComment(
        Guid id,
        [FromBody] AddCommentRequest request,
        CancellationToken cancellationToken)
    {
        var comment = await sender.Send(
            new AddCommentCommand(id, request.Body ?? string.Empty, request.IsInternal ?? false),
            cancellationToken);
        return Created($"/api/v1/incidents/{id}/comments", comment);
    }

    [HttpGet("{id:guid}/history")]
    [Authorize(Policy = Policies.CanViewIncident)]
    [ProducesResponseType<Paged<HistoryEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<Paged<HistoryEntryDto>>> ListHistory(
        Guid id,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken,
        [FromQuery] int limit = PageLimits.Default,
        [FromQuery] bool includeTotal = false) =>
        Ok(await sender.Send(new ListHistoryQuery(id, cursor, limit, includeTotal), cancellationToken));

    private static bool TryParseSort(string? token, out IncidentSort sort)
    {
        sort = IncidentSort.CreatedAtDesc;
        switch (token)
        {
            case null or "" or "-createdAt":
                return true;
            case "createdAt":
                sort = IncidentSort.CreatedAtAsc;
                return true;
            case "severity":
                sort = IncidentSort.Severity;
                return true;
            case "-updatedAt":
                sort = IncidentSort.UpdatedAtDesc;
                return true;
            default:
                return false;
        }
    }
}
