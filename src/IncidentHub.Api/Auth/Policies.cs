using IncidentHub.Domain.Users;
using Microsoft.AspNetCore.Authorization;

namespace IncidentHub.Api.Auth;

/// <summary>
/// Policy names and registration. Policies check scope + role only; the application-team check is a
/// resource check done with <see cref="AuthorizationServiceExtensions.AuthorizeAppAsync"/>.
/// </summary>
public static class Policies
{
    public const string CanReportIncident = nameof(CanReportIncident);
    public const string CanViewIncident = nameof(CanViewIncident);
    public const string CanTransitionIncident = nameof(CanTransitionIncident);
    public const string CanAssignIncident = nameof(CanAssignIncident);
    public const string CanCommentInternally = nameof(CanCommentInternally);
    public const string CanManageApplications = nameof(CanManageApplications);
    public const string CanManageTeams = nameof(CanManageTeams);
    public const string CanViewDashboard = nameof(CanViewDashboard);

    private static readonly string[] AnyRole = Enum.GetNames<UserRole>();

    private static readonly string[] ResponderRoles =
        [nameof(UserRole.Responder), nameof(UserRole.TeamLead), nameof(UserRole.Admin)];

    private static readonly string[] LeadRoles = [nameof(UserRole.TeamLead), nameof(UserRole.Admin)];

    private static readonly string[] AdminOnly = [nameof(UserRole.Admin)];

    public static IServiceCollection AddIncidentHubAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(Base(new AuthorizationPolicyBuilder()).Build())
            .SetFallbackPolicy(Base(new AuthorizationPolicyBuilder()).Build())
            .AddPolicy(CanReportIncident, p => Base(p).RequireRole(AnyRole))
            .AddPolicy(CanViewIncident, p => Base(p).RequireRole(AnyRole))
            .AddPolicy(CanTransitionIncident, p => Base(p).RequireRole(AnyRole))
            .AddPolicy(CanAssignIncident, p => Base(p).RequireRole(ResponderRoles))
            .AddPolicy(CanCommentInternally, p => Base(p).RequireRole(ResponderRoles))
            .AddPolicy(CanManageApplications, p => Base(p).RequireRole(LeadRoles))
            .AddPolicy(CanManageTeams, p => Base(p).RequireRole(AdminOnly))
            .AddPolicy(CanViewDashboard, p => Base(p).RequireRole(AnyRole));

        services.AddScoped<IAuthorizationHandler, AppTeamAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationResultHandler>();

        return services;
    }

    private static AuthorizationPolicyBuilder Base(AuthorizationPolicyBuilder builder) =>
        builder
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => ctx.User.HasScope(AuthClaims.RequiredScope));
}
