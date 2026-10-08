using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace IncidentHub.Api.Errors;

/// <summary>
/// Maps unhandled exceptions to RFC 7807 responses with a <c>traceId</c>.
/// Domain <c>Result&lt;T&gt;</c> / exception mappings (422, 403 transition, 409) are added here as they are introduced.
/// </summary>
public sealed class ProblemDetailsMiddleware(
    RequestDelegate next,
    IProblemDetailsService problemDetails,
    ILogger<ProblemDetailsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client went away; nothing to write.
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path.Value);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred.",
                },
            });
        }
    }
}

public static class ProblemDetailsSetup
{
    public static IServiceCollection AddIncidentHubProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] =
                    Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier);
}
