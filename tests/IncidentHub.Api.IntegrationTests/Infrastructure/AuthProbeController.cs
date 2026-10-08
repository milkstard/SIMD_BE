using IncidentHub.Api.Auth;
using IncidentHub.Api.Hubs;
using IncidentHub.Application.Incidents.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace IncidentHub.Api.IntegrationTests.Infrastructure;

/// <summary>Test-only endpoints, one per policy, registered by <see cref="ApiFactory"/>. Never part of the production app.</summary>
[ApiController]
[Route("probe")]
public sealed class AuthProbeController(
    IAuthorizationService authorization,
    IHubContext<IncidentsHub, IIncidentClient> hub) : ControllerBase
{
    public sealed record CommentRequest(string Body, bool IsInternal);

    /// <summary>No attribute: only the fallback policy applies.</summary>
    [HttpGet("fallback")]
    public IActionResult Fallback() => Ok();

    [HttpGet("anonymous")]
    [AllowAnonymous]
    public IActionResult Anonymous() => Ok();

    [HttpGet("policy/report")]
    [Authorize(Policy = Policies.CanReportIncident)]
    public IActionResult Report() => Ok();

    [HttpGet("policy/view")]
    [Authorize(Policy = Policies.CanViewIncident)]
    public IActionResult View() => Ok();

    [HttpGet("policy/transition")]
    [Authorize(Policy = Policies.CanTransitionIncident)]
    public IActionResult Transition() => Ok();

    [HttpGet("policy/assign")]
    [Authorize(Policy = Policies.CanAssignIncident)]
    public IActionResult Assign() => Ok();

    [HttpGet("policy/comment-internal")]
    [Authorize(Policy = Policies.CanCommentInternally)]
    public IActionResult CommentInternally() => Ok();

    [HttpGet("policy/manage")]
    [Authorize(Policy = Policies.CanManageApplications)]
    public IActionResult Manage() => Ok();

    [HttpGet("policy/dashboard")]
    [Authorize(Policy = Policies.CanViewDashboard)]
    public IActionResult Dashboard() => Ok();

    /// <summary>Read of an application-scoped resource: non-members must not learn it exists (404).</summary>
    [HttpGet("apps/{applicationId:guid}/incident")]
    [Authorize(Policy = Policies.CanViewIncident)]
    public async Task<IActionResult> ReadIncident(Guid applicationId, CancellationToken cancellationToken) =>
        await authorization.AuthorizeAppAsync(User, applicationId, AppTeamRequirement.Member, cancellationToken)
            ? Ok()
            : NotFound();

    /// <summary>Write on an application-scoped resource: non-members get 403.</summary>
    [HttpPost("apps/{applicationId:guid}/transition")]
    [Authorize(Policy = Policies.CanTransitionIncident)]
    public async Task<IActionResult> WriteIncident(Guid applicationId, CancellationToken cancellationToken) =>
        await authorization.AuthorizeAppAsync(User, applicationId, AppTeamRequirement.Member, cancellationToken)
            ? Ok()
            : Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");

    /// <summary>Application management: Admin may act on any application, TeamLead only on their own.</summary>
    [HttpPost("apps/{applicationId:guid}/manage")]
    [Authorize(Policy = Policies.CanManageApplications)]
    public async Task<IActionResult> ManageApp(Guid applicationId, CancellationToken cancellationToken) =>
        await authorization.AuthorizeAppAsync(User, applicationId, AppTeamRequirement.MemberOrAdmin, cancellationToken)
            ? Ok()
            : Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");

    /// <summary>
    /// Stand-in for the future AddComment broadcast: internal comments go to the responders group only,
    /// public ones to the whole team group. Exercises the hub's group membership rules.
    /// </summary>
    [HttpPost("apps/{applicationId:guid}/comments")]
    [AllowAnonymous]
    public async Task<IActionResult> Broadcast(Guid applicationId, CommentRequest request)
    {
        var group = request.IsInternal ? HubGroups.Responders(applicationId) : HubGroups.App(applicationId);
        var dto = new CommentDto(Guid.NewGuid(), "INC-1", request.Body, request.IsInternal, DateTimeOffset.UtcNow);
        await hub.Clients.Group(group).CommentAdded(dto);
        return Ok();
    }
}
