using IncidentHub.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace IncidentHub.Api.Swagger;

public static class SwaggerSetup
{
    private const string SchemeName = "oauth2";

    public static IServiceCollection AddIncidentHubSwagger(this IServiceCollection services, IConfiguration configuration)
    {
        var tenant = configuration[$"{AzureAdSettings.SectionName}:TenantId"] ?? "{tenant-id}";
        var clientId = configuration[$"{AzureAdSettings.SectionName}:ClientId"] ?? "{api-client-id}";
        var authority = $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0";
        var scope = $"api://{clientId}/{AuthClaims.RequiredScope}";

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "IncidentHub API", Version = "v1" });
            options.AddSecurityDefinition(SchemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri($"{authority}/authorize"),
                        TokenUrl = new Uri($"{authority}/token"),
                        Scopes = new Dictionary<string, string> { [scope] = "Access IncidentHub as the signed-in user" },
                    },
                },
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = SchemeName } }] = [scope],
            });
            options.OperationFilter<AuthResponsesOperationFilter>();
        });

        return services;
    }

    public static IApplicationBuilder UseIncidentHubSwagger(this WebApplication app, IConfiguration configuration)
    {
        var scope = ApiScope(configuration);

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.OAuthClientId(configuration["Swagger:ClientId"]);
            options.OAuthUsePkce();
            options.OAuthScopes(scope);

            // Swagger UI's code-redemption request omits `scope`, which Entra's v2 token endpoint requires (AADSTS900144).
            // Use single quotes only: Swashbuckle embeds this script in JSON.parse('...'), and a double quote breaks the
            // JSON and leaves the UI blank. The scope is restricted to safe characters so it can't break out of the string.
            if (scope.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '/' or '.' or '_' or '-'))
            {
                options.UseRequestInterceptor(
                    "(req) => { if (req.url && req.url.includes('/oauth2/v2.0/token') && typeof req.body === 'string' "
                    + "&& !req.body.includes('scope=')) { req.body += '&scope=' + encodeURIComponent('"
                    + scope
                    + "'); } return req; }");
            }
        });
        return app;
    }

    private static string ApiScope(IConfiguration configuration) =>
        $"api://{configuration[$"{AzureAdSettings.SectionName}:ClientId"]}/{AuthClaims.RequiredScope}";
}

/// <summary>Documents the 401/403 ProblemDetails responses on every operation that is not anonymous.</summary>
public sealed class AuthResponsesOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var anonymous = context.MethodInfo.GetCustomAttributes(true)
            .Concat(context.MethodInfo.DeclaringType?.GetCustomAttributes(true) ?? [])
            .OfType<AllowAnonymousAttribute>()
            .Any();
        if (anonymous)
        {
            return;
        }

        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid access token (ProblemDetails)" });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Authenticated but not permitted (ProblemDetails)" });
    }
}
