using System.Diagnostics;
using FluentValidation;
using IncidentHub.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Api.Errors;

/// <summary>
/// Maps exceptions to RFC 7807 responses with a <c>traceId</c>: validation to 422, not found to 404,
/// conflict or stale write to 409, anything else to 500.
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
        catch (ValidationException exception) when (!context.Response.HasStarted)
        {
            var errors = exception.Errors
                .GroupBy(e => ToCamelCase(e.PropertyName))
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

            await WriteAsync(context, new HttpValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Validation failed",
            });
        }
        catch (NotFoundException exception) when (!context.Response.HasStarted)
        {
            await WriteAsync(context, new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not found",
                Detail = exception.Message,
            });
        }
        catch (ConflictException exception) when (!context.Response.HasStarted)
        {
            await WriteAsync(context, new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = exception.Message,
            });
        }
        catch (DbUpdateConcurrencyException) when (!context.Response.HasStarted)
        {
            await WriteAsync(context, new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = "The resource was modified by someone else. Refetch and retry.",
            });
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path.Value);

            await WriteAsync(context, new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
            });
        }
    }

    private async Task WriteAsync(HttpContext context, ProblemDetails details)
    {
        context.Response.Clear();
        context.Response.StatusCode = details.Status ?? StatusCodes.Status500InternalServerError;
        await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = details });
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}

public static class ProblemDetailsSetup
{
    public static IServiceCollection AddIncidentHubProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] =
                    Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier);
}
