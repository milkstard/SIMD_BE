using System.Security.Claims;
using IncidentHub.Domain.Users;

namespace IncidentHub.Api.Auth;

/// <summary>Claim names as they appear in the Entra JWT (inbound claim mapping is disabled) plus our own.</summary>
public static class AuthClaims
{
    public const string ObjectId = "oid";
    public const string TenantId = "tid";
    public const string Groups = "groups";
    public const string HasGroups = "hasgroups";
    public const string ClaimNames = "_claim_names";
    public const string Scope = "scp";
    public const string Name = "name";

    /// <summary>Internal <c>User.Id</c>, added by <see cref="GroupRoleClaimsTransformation"/>.</summary>
    public const string UserId = "uid";

    /// <summary>Marks a principal that already went through the claims transformation.</summary>
    public const string TransformedMarker = "ih_transformed";

    public const string RequiredScope = "access_as_user";

    public static readonly string[] EmailClaimTypes = ["preferred_username", "email", "upn"];
}

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(AuthClaims.UserId), out var id) ? id : null;

    public static IReadOnlySet<UserRole> GetRoles(this ClaimsPrincipal principal)
    {
        var roles = new HashSet<UserRole>();
        foreach (var claim in principal.FindAll(ClaimTypes.Role))
        {
            if (Enum.TryParse<UserRole>(claim.Value, ignoreCase: false, out var role) && Enum.IsDefined(role))
            {
                roles.Add(role);
            }
        }

        return roles;
    }

    public static bool HasScope(this ClaimsPrincipal principal, string scope) =>
        principal.FindAll(AuthClaims.Scope)
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}
