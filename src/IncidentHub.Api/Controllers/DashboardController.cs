using IncidentHub.Api.Auth;
using IncidentHub.Api.Swagger;
using IncidentHub.Application.Dashboard.Dtos;
using IncidentHub.Application.Dashboard.GetAppStatus;
using IncidentHub.Application.Dashboard.GetDashboardSummary;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IncidentHub.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[Authorize]
public sealed class DashboardController(ISender sender) : ControllerBase
{
    [HttpGet("app-status")]
    [ProducesResponseType<IReadOnlyList<AppStatusItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AppStatusItemDto>>> GetAppStatus(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetAppStatusQuery(), cancellationToken));

    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary([FromQuery, OpenApiAllowedValues("24h", "7d", "30d", Default = "7d")] string? range, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetDashboardSummaryQuery(range), cancellationToken));
}
