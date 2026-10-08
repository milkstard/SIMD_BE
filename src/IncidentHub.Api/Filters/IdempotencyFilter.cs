using System.Text.Json;
using IncidentHub.Api.Auth;
using IncidentHub.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace IncidentHub.Api.Filters;

/// <summary>Opt-in for create endpoints: honours the <c>Idempotency-Key</c> header (a uuid).</summary>
public sealed class IdempotentAttribute() : TypeFilterAttribute(typeof(IdempotencyFilter));

/// <summary>
/// Replays the original 2xx response for a repeated key; a request still running with the same key gets 409.
/// Keys are scoped to the user and path so one client cannot replay the response of another user.
/// </summary>
public sealed class IdempotencyFilter(IIdempotencyStore store, IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> json)
    : IAsyncActionFilter
{
    private static readonly TimeSpan CompletedTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(2);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var header) || string.IsNullOrWhiteSpace(header))
        {
            await next();
            return;
        }

        if (!Guid.TryParse(header.ToString(), out var keyValue))
        {
            context.Result = Problem(http, StatusCodes.Status400BadRequest, "Invalid Idempotency-Key", "Idempotency-Key must be a uuid.");
            return;
        }

        var ct = http.RequestAborted;
        var key = $"{http.User.GetUserId():N}:{http.Request.Path}:{keyValue:N}";

        var completed = await store.GetAsync(key, ct);
        if (completed is not null)
        {
            if (completed.Location is not null)
            {
                http.Response.Headers.Location = completed.Location;
            }

            context.Result = new ContentResult
            {
                StatusCode = completed.Status,
                Content = completed.Body,
                ContentType = "application/json; charset=utf-8",
            };
            return;
        }

        if (!await store.TryBeginAsync(key, PendingTtl, ct))
        {
            context.Result = Problem(http, StatusCodes.Status409Conflict, "Request in progress",
                "A request with this Idempotency-Key is already being processed.");
            return;
        }

        var executed = await next();

        if (executed.Exception is not null || executed.Result is not ObjectResult { StatusCode: >= 200 and < 300 } result)
        {
            await store.ReleaseAsync(key, CancellationToken.None);
            return;
        }

        var location = (result as CreatedResult)?.Location;
        var body = JsonSerializer.Serialize(result.Value, result.Value?.GetType() ?? typeof(object), json.Value.JsonSerializerOptions);
        await store.CompleteAsync(key, new IdempotentResponse(result.StatusCode.Value, location, body), CompletedTtl, CancellationToken.None);
    }

    private static ObjectResult Problem(HttpContext http, int status, string title, string detail)
    {
        var factory = http.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(http, status, title, detail: detail);
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
