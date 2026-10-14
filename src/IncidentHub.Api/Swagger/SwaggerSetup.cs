using IncidentHub.Api.Auth;
using IncidentHub.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Any;
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
            options.OrderActionsBy(description => $"{description.RelativePath}|{description.HttpMethod}");
            options.OperationFilter<AuthResponsesOperationFilter>();
            options.OperationFilter<RequestHeadersOperationFilter>();
            options.OperationFilter<MultipartUploadOperationFilter>();
            options.ParameterFilter<AllowedValuesParameterFilter>();
            options.DocumentFilter<SortedDocumentFilter>();
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

/// <summary>Documents the <c>If-Match</c> (required) and <c>Idempotency-Key</c> (optional) request headers.</summary>
public sealed class RequestHeadersOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var attributes = context.MethodInfo.GetCustomAttributes(true);

        if (attributes.OfType<RequireIfMatchAttribute>().Any())
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "If-Match",
                In = ParameterLocation.Header,
                Required = true,
                Description = "Quoted rowVersion from the last read (the ETag header).",
                Schema = new OpenApiSchema { Type = "string" },
            });
        }

        if (attributes.OfType<IdempotentAttribute>().Any())
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "Idempotency-Key",
                In = ParameterLocation.Header,
                Required = false,
                Description = "Repeating a key returns the original result instead of creating another resource.",
                Schema = new OpenApiSchema { Type = "string", Format = "uuid" },
            });
        }
    }
}

/// <summary>Describes the body of an action that reads multipart itself (<see cref="DisableFormValueModelBindingAttribute"/>): one binary <c>file</c>.</summary>
public sealed class MultipartUploadOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.MethodInfo.GetCustomAttributes(true).OfType<DisableFormValueModelBindingAttribute>().Any())
        {
            return;
        }

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content =
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Required = new HashSet<string> { MultipartFileReader.FieldName },
                        Properties = { [MultipartFileReader.FieldName] = new OpenApiSchema { Type = "string", Format = "binary" } },
                    },
                },
            },
        };
    }
}

/// <summary>Marks a string parameter as accepting only these values in the OpenAPI document. The server still validates it itself.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class OpenApiAllowedValuesAttribute(params string[] values) : Attribute
{
    public IReadOnlyList<string> Values { get; } = values;

    /// <summary>Documented default, or <c>null</c> for none.</summary>
    public string? Default { get; init; }
}

/// <summary>Documents the values of a parameter marked with <see cref="OpenApiAllowedValuesAttribute"/> as an enum.</summary>
public sealed class AllowedValuesParameterFilter : IParameterFilter
{
    public void Apply(OpenApiParameter parameter, ParameterFilterContext context)
    {
        var attribute = context.ParameterInfo?.GetCustomAttributes(true).OfType<OpenApiAllowedValuesAttribute>().SingleOrDefault();
        if (attribute is null)
        {
            return;
        }

        parameter.Schema.Enum = attribute.Values.Select(v => (IOpenApiAny)new OpenApiString(v)).ToList();
        if (attribute.Default is not null)
        {
            parameter.Schema.Default = new OpenApiString(attribute.Default);
        }
    }
}

/// <summary>Sorts paths and component schemas ordinally so the generated document does not depend on reflection order.</summary>
public sealed class SortedDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var paths = swaggerDoc.Paths.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        swaggerDoc.Paths = new OpenApiPaths();
        foreach (var (key, item) in paths)
        {
            swaggerDoc.Paths.Add(key, item);
        }

        var schemas = swaggerDoc.Components.Schemas.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        swaggerDoc.Components.Schemas.Clear();
        foreach (var (key, schema) in schemas)
        {
            swaggerDoc.Components.Schemas.Add(key, schema);
        }
    }
}
