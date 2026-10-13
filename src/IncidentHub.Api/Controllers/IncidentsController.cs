using IncidentHub.Api.Auth;
using IncidentHub.Api.Contracts;
using IncidentHub.Api.Filters;
using IncidentHub.Application.Common.Paging;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Application.Incidents.FindSimilarIncidents;
using IncidentHub.Application.Incidents.GetIncident;
using IncidentHub.Application.Incidents.ListIncidents;
using IncidentHub.Application.Incidents.ReportIncident;
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
