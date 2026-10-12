using System.Diagnostics;
using FluentValidation;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Domain.Incidents;
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
    private const string InvalidTransitionType = "https://incidenthub/errors/invalid-transition";

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
        catch (ForbiddenException exception) when (!context.Response.HasStarted)
        {
            await WriteAsync(context, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = exception.Message,
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
        catch (TransitionRequirementException exception) when (!context.Response.HasStarted)
        {
            await WriteAsync(context, new HttpValidationProblemDetails(
                new Dictionary<string, string[]> { [exception.Field] = [exception.Message] })
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Validation failed",
            });
        }
        catch (TransitionNotPermittedException exception) when (!context.Response.HasStarted)
        {
            await WriteAsync(context, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Transition not permitted",
                Detail = exception.Message,
            });
        }
        catch (Exception exception) when (exception is InvalidTransitionException or IncidentClosedException && !context.Response.HasStarted)
        {
            await WriteAsync(context, new ProblemDetails
            {
                Type = InvalidTransitionType,
                Status = StatusCodes.Status409Conflict,
                Title = "Transition not allowed",
                Detail = exception.Message,
            });
        }
        catch (DuplicateIncidentException exception) when (exception.ExistingIncidentNumber is not null && !context.Response.HasStarted)
        {
            var details = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Duplicate incident",
                Detail = exception.Message,
            };
            details.Extensions["existingIncidentNumber"] = exception.ExistingIncidentNumber;
            await WriteAsync(context, details);
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
        catch (BadHttpRequestException exception) when (!context.Response.HasStarted)
        {
            // Raised by the server while the body is read, e.g. 413 when [RequestSizeLimit] is exceeded.
            await WriteAsync(context, new ProblemDetails
            {
                Status = exception.StatusCode is >= 400 and < 500 ? exception.StatusCode : StatusCodes.Status400BadRequest,
                Title = exception.StatusCode == StatusCodes.Status413PayloadTooLarge ? "Request body too large" : "Bad request",
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
