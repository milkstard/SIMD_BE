using IncidentHub.Api.Auth;
using IncidentHub.Api.Contracts;
using IncidentHub.Api.Filters;
using IncidentHub.Application.Sla;
using IncidentHub.Application.Sla.GetSlaPolicy;
using IncidentHub.Application.Sla.ListSlaPolicies;
using IncidentHub.Application.Sla.UpdateSlaPolicy;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IncidentHub.Api.Controllers;

[ApiController]
[Route("api/v1/sla-policies")]
[Authorize(Policy = Policies.CanManageSlaPolicies)]
public sealed class SlaPoliciesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SlaPolicyDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SlaPolicyDto>>> List(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListSlaPoliciesQuery(), cancellationToken));

    [HttpGet("{id}")]
    [ProducesResponseType<SlaPolicyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SlaPolicyDto>> Get(string id, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSlaPolicyQuery(id), cancellationToken));

    [HttpPut("{id}")]
    [RequireIfMatch]
    [ProducesResponseType<SlaPolicyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<SlaPolicyDto>> Update(
        string id,
        [FromBody] UpdateSlaPolicyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateSlaPolicyCommand(
                id,
                request.AckMinutes,
                request.ResolveMinutes,
                request.EscalateAtPercent,
                HttpContext.GetIfMatchRowVersion()),
            cancellationToken));
}
