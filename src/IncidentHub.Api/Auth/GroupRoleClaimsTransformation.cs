using System.Security.Claims;
using IncidentHub.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace IncidentHub.Api.Auth;

/// <summary>
/// Maps Entra group ids to <see cref="ClaimTypes.Role"/> claims and adds the internal user id (<c>uid</c>).
/// Idempotent: the framework may call it several times per request on the same principal.
/// </summary>
public sealed class GroupRoleClaimsTransformation(
    IOptions<GroupRoleOptions> options,
    IUserDirectory users,
    IHttpContextAccessor httpContextAccessor,
    ILogger<GroupRoleClaimsTransformation> logger) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.HasClaim(c => c.Type == AuthClaims.TransformedMarker))
        {
            return principal;
        }

        var claims = new List<Claim> { new(AuthClaims.TransformedMarker, "1") };
        var objectId = principal.FindFirstValue(AuthClaims.ObjectId);

        if (string.IsNullOrWhiteSpace(objectId))
        {
            logger.LogWarning("Token has no oid claim; no roles granted");
            return WithIdentity(principal, claims);
        }

        var cancellationToken = httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var displayName = principal.FindFirstValue(AuthClaims.Name) ?? objectId;
        var email = AuthClaims.EmailClaimTypes
            .Select(principal.FindFirstValue)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

        var userId = await users.EnsureUserAsync(objectId, displayName, email, cancellationToken);
        claims.Add(new Claim(AuthClaims.UserId, userId.ToString()));

        var groups = principal.FindAll(AuthClaims.Groups).Select(c => c.Value).ToList();
        if (groups.Count == 0 && HasGroupOverage(principal))
        {
            // Overage resolution via Microsoft Graph is deferred: fail closed.
            logger.LogWarning("Group overage for {UserId}; no roles granted", userId);
        }

        var map = new Dictionary<string, Domain.Users.UserRole>(options.Value.GroupRoleMap, StringComparer.OrdinalIgnoreCase);
        var roles = groups
            .Where(map.ContainsKey)
            .Select(g => map[g])
            .Distinct();

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r.ToString())));

        return WithIdentity(principal, claims);
    }

    private static bool HasGroupOverage(ClaimsPrincipal principal) =>
        principal.HasClaim(c => c.Type == AuthClaims.HasGroups && string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase))
        || principal.FindAll(AuthClaims.ClaimNames).Any(c => c.Value.Contains("\"groups\"", StringComparison.Ordinal));

    private static ClaimsPrincipal WithIdentity(ClaimsPrincipal principal, List<Claim> claims)
    {
        principal.AddIdentity(new ClaimsIdentity(claims, authenticationType: "IncidentHub"));
        return principal;
    }
}
