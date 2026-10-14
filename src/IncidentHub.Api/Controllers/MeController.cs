using IncidentHub.Application.Users.GetCurrentUser;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IncidentHub.Api.Controllers;

[ApiController]
[Route("api/v1/me")]
[Authorize]
public sealed class MeController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CurrentUserDto>> Get(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetCurrentUserQuery(), cancellationToken));
}
