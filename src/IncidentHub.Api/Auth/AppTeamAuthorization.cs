using System.Security.Claims;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Users;
using Microsoft.AspNetCore.Authorization;

namespace IncidentHub.Api.Auth;

public interface IAppScoped
{
    Guid ApplicationId { get; }

    CancellationToken CancellationToken { get; }
}

/// <summary>Authorization resource: the monitored application a request targets (never an EF entity).</summary>
public sealed record AppScopedResource(Guid ApplicationId, CancellationToken CancellationToken = default) : IAppScoped;

/// <summary>User must belong to the application's team. Admin skips the check only when <paramref name="AllowAdminBypass"/>.</summary>
public sealed record AppTeamRequirement(bool AllowAdminBypass) : IAuthorizationRequirement
{
    /// <summary>Incident actions: team membership always required, Admin included.</summary>
    public static readonly AppTeamRequirement Member = new(AllowAdminBypass: false);

    /// <summary>Application management and dashboard: Admin may act on any application.</summary>
    public static readonly AppTeamRequirement MemberOrAdmin = new(AllowAdminBypass: true);
}

public sealed class AppTeamAuthorizationHandler(
    IAppTeamReader teamReader,
    ILogger<AppTeamAuthorizationHandler> logger) : AuthorizationHandler<AppTeamRequirement, IAppScoped>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AppTeamRequirement requirement,
        IAppScoped resource)
    {
        if (requirement.AllowAdminBypass && context.User.IsInRole(nameof(UserRole.Admin)))
        {
            context.Succeed(requirement);
            return;
        }

        var userId = context.User.GetUserId();
        if (userId is null)
        {
            context.Fail();
            return;
        }

        var isMember = await teamReader.IsMemberAsync(userId.Value, resource.ApplicationId, resource.CancellationToken);
        if (isMember)
        {
            context.Succeed(requirement);
            return;
        }

        logger.LogWarning(
            "Authorization denied for user {UserId} on application {ApplicationId} (admin bypass {AdminBypass})",
            userId,
            resource.ApplicationId,
            requirement.AllowAdminBypass);
        context.Fail();
    }
}

public static class AuthorizationServiceExtensions
{
    /// <summary>Resource check: does <paramref name="user"/> belong to the team of <paramref name="applicationId"/>?</summary>
    public static async Task<bool> AuthorizeAppAsync(
        this IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        Guid applicationId,
        AppTeamRequirement requirement,
        CancellationToken cancellationToken)
    {
        var result = await authorizationService.AuthorizeAsync(
            user,
            new AppScopedResource(applicationId, cancellationToken),
            requirement);
        return result.Succeeded;
    }
}
