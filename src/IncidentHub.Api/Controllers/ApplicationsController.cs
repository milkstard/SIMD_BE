using IncidentHub.Api.Auth;
using IncidentHub.Api.Contracts;
using IncidentHub.Api.Filters;
using IncidentHub.Application.Apps;
using IncidentHub.Application.Apps.CreateApp;
using IncidentHub.Application.Apps.GetApp;
using IncidentHub.Application.Apps.ListApps;
using IncidentHub.Application.Apps.UpdateApp;
using IncidentHub.Application.Common.Paging;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IncidentHub.Api.Controllers;

[ApiController]
[Route("api/v1/applications")]
public sealed class ApplicationsController(ISender sender, IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.CanViewIncident)]
    [ProducesResponseType<Paged<ApplicationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<Paged<ApplicationDto>>> List(
        [FromQuery] string? cursor,
        [FromQuery] int limit = PageLimits.Default,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new ListAppsQuery(cursor, limit, includeInactive), cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.CanViewIncident)]
    [ProducesResponseType<ApplicationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetAppQuery(id), cancellationToken));

    [HttpPost]
    [Authorize(Policy = Policies.CanCreateApplications)]
    [Idempotent]
    [ProducesResponseType<ApplicationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApplicationDto>> Create(
        [FromBody] CreateApplicationRequest request,
        CancellationToken cancellationToken)
    {
        var app = await sender.Send(
            new CreateAppCommand(
                request.Name ?? string.Empty,
                request.Code ?? string.Empty,
                request.OwningTeamId ?? Guid.Empty,
                request.EscalationContactId,
                request.Environments ?? [],
                request.IsActive ?? true),
            cancellationToken);
        return Created($"/api/v1/applications/{app.Id}", app);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.CanManageApplications)]
    [RequireIfMatch]
    [ProducesResponseType<ApplicationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<ApplicationDto>> Update(
        Guid id,
        [FromBody] UpdateApplicationRequest request,
        CancellationToken cancellationToken)
    {
        // Admin may manage any application; a TeamLead only one owned by a team they belong to.
        if (!await authorization.AuthorizeAppAsync(User, id, AppTeamRequirement.MemberOrAdmin, cancellationToken))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");
        }

        return Ok(await sender.Send(
            new UpdateAppCommand(
                id,
                request.Name ?? string.Empty,
                request.Code ?? string.Empty,
                request.OwningTeamId ?? Guid.Empty,
                request.EscalationContactId,
                request.Environments ?? [],
                request.IsActive ?? true,
                HttpContext.GetIfMatchRowVersion()),
            cancellationToken));
    }
}
