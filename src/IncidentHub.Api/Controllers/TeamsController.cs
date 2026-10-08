using IncidentHub.Api.Auth;
using IncidentHub.Api.Contracts;
using IncidentHub.Api.Filters;
using IncidentHub.Application.Common.Paging;
using IncidentHub.Application.Teams;
using IncidentHub.Application.Teams.CreateTeam;
using IncidentHub.Application.Teams.GetTeam;
using IncidentHub.Application.Teams.ListTeams;
using IncidentHub.Application.Teams.UpdateTeam;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IncidentHub.Api.Controllers;

[ApiController]
[Route("api/v1/teams")]
[Authorize(Policy = Policies.CanManageTeams)]
public sealed class TeamsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<Paged<TeamDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<Paged<TeamDto>>> List(
        [FromQuery] string? cursor,
        [FromQuery] int limit = PageLimits.Default,
        [FromQuery] bool includeTotal = false,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new ListTeamsQuery(cursor, limit, includeTotal), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<TeamDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TeamDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetTeamQuery(id), cancellationToken));

    [HttpPost]
    [Idempotent]
    [ProducesResponseType<TeamDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TeamDto>> Create([FromBody] CreateTeamRequest request, CancellationToken cancellationToken)
    {
        var team = await sender.Send(new CreateTeamCommand(request.Name, request.Email, request.TeamsChannelUrl), cancellationToken);
        return Created($"/api/v1/teams/{team.Id}", team);
    }

    [HttpPut("{id:guid}")]
    [RequireIfMatch]
    [ProducesResponseType<TeamDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<TeamDto>> Update(
        Guid id,
        [FromBody] UpdateTeamRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateTeamCommand(id, request.Name, request.Email, request.TeamsChannelUrl, HttpContext.GetIfMatchRowVersion()),
            cancellationToken));
}
