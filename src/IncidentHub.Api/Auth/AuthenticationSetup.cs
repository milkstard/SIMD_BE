using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace IncidentHub.Api.Auth;

public static class AuthenticationSetup
{
    private const string HubPathPrefix = "/hubs";

    public static IServiceCollection AddIncidentHubAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<AzureAdSettings>, AzureAdSettingsValidator>();
        services.AddSingleton<IValidateOptions<GroupRoleOptions>, GroupRoleOptionsValidator>();
        services.AddOptions<AzureAdSettings>().Bind(configuration.GetSection(AzureAdSettings.SectionName)).ValidateOnStart();
        services.AddOptions<GroupRoleOptions>().Bind(configuration.GetSection(GroupRoleOptions.SectionName)).ValidateOnStart();

        // Delegate overload: configuration is read when the options are first built, not while services are registered.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(
                _ => { },
                identity => configuration.GetSection(AzureAdSettings.SectionName).Bind(identity));

        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, ConfigureJwtBearer);

        services.AddHttpContextAccessor();
        services.AddScoped<IClaimsTransformation, GroupRoleClaimsTransformation>();
        services.AddScoped<Application.Abstractions.ICurrentUser, CurrentUser>();

        return services;
    }

    private static void ConfigureJwtBearer(JwtBearerOptions options)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
        options.TokenValidationParameters.NameClaimType = AuthClaims.Name;
        options.TokenValidationParameters.ClockSkew = TimeSpan.FromMinutes(2);

        var events = options.Events ??= new JwtBearerEvents();
        var previousMessageReceived = events.OnMessageReceived;
        var previousTokenValidated = events.OnTokenValidated;

        events.OnMessageReceived = async context =>
        {
            await previousMessageReceived(context);

            // Browsers cannot set headers on WebSocket requests; accept the token from the query string for hubs only.
            if (string.IsNullOrEmpty(context.Token)
                && context.Request.Path.StartsWithSegments(HubPathPrefix, StringComparison.OrdinalIgnoreCase)
                && context.Request.Query.TryGetValue("access_token", out var token))
            {
                context.Token = token;
            }
        };

        events.OnTokenValidated = async context =>
        {
            await previousTokenValidated(context);

            var settings = context.HttpContext.RequestServices.GetRequiredService<IOptions<AzureAdSettings>>().Value;
            var tenant = context.Principal?.FindFirstValue(AuthClaims.TenantId);
            if (!string.Equals(tenant, settings.TenantId, StringComparison.OrdinalIgnoreCase))
            {
                context.Fail("Token was issued by a different tenant.");
            }
        };

        events.OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("IncidentHub.Api.Auth");
            logger.LogWarning("JWT authentication failed: {Reason}", context.Exception.GetType().Name);
            return Task.CompletedTask;
        };

        events.OnChallenge = async context =>
        {
            context.HandleResponse();

            var response = context.Response;
            response.StatusCode = StatusCodes.Status401Unauthorized;
            response.Headers.WWWAuthenticate = context.AuthenticateFailure is null
                ? "Bearer"
                : "Bearer error=\"invalid_token\"";

            var problemDetails = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
            await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context.HttpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Unauthorized",
                    Detail = "Authentication is required to access this resource.",
                },
            });
        };
    }
}
