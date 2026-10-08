using System.Security.Claims;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Users;

namespace IncidentHub.Api.Auth;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal Principal => httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
        ? user
        : throw new InvalidOperationException("There is no authenticated user for the current request.");

    public Guid UserId => Principal.GetUserId()
        ?? throw new InvalidOperationException("The authenticated user has no internal user id (token without oid?).");

    public string EntraObjectId => Principal.FindFirstValue(AuthClaims.ObjectId) ?? string.Empty;

    public string DisplayName => Principal.FindFirstValue(AuthClaims.Name) ?? string.Empty;

    public string Email => AuthClaims.EmailClaimTypes
        .Select(Principal.FindFirstValue)
        .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    public IReadOnlySet<UserRole> Roles => Principal.GetRoles();

    public bool IsInRole(UserRole role) => Roles.Contains(role);

    public Actor ToActor() => new(UserId, Roles);
}
