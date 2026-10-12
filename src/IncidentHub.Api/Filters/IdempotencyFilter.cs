using System.Security.Cryptography;
using System.Text.Json;
using IncidentHub.Api.Auth;
using IncidentHub.Application.Abstractions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace IncidentHub.Api.Filters;

/// <summary>Opt-in for create endpoints: honours the <c>Idempotency-Key</c> header (a uuid).</summary>
public sealed class IdempotentAttribute() : TypeFilterAttribute(typeof(IdempotencyFilter));

/// <summary>
/// Replays the original 2xx response for a repeated key; a request still running with the same key gets 409.
/// A repeated key sent with a different body gets 422: the body is hashed with SHA-256 (so it must be byte-identical) before model binding reads it,
/// after the endpoint's <c>[RequestSizeLimit]</c> (or a 1 MiB default) has been enforced. Larger bodies get 413.
/// Keys are scoped to the user and path so one client cannot replay the response of another user.
/// </summary>
public sealed class IdempotencyFilter(IIdempotencyStore store, IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> json)
    : IAsyncResourceFilter, IAsyncActionFilter
{
    public const string RequestHashItem = "Idempotency.RequestHash";

    /// <summary>Body cap for an idempotent endpoint that declares no <c>[RequestSizeLimit]</c> of its own.</summary>
    private const long DefaultMaxBodyBytes = 1024 * 1024;

    private static readonly TimeSpan CompletedTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(2);

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var request = http.Request;
        if (request.Headers.ContainsKey("Idempotency-Key"))
        {
            // This filter runs before [RequestSizeLimit] (Order 900) would apply, so it enforces the endpoint's limit itself
            // before it reads and buffers anything.
            var limit = http.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize ?? DefaultMaxBodyBytes;
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
            {
                feature.MaxRequestBodySize = limit;
            }

            if (request.ContentLength > limit)
            {
                throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);
            }

            request.EnableBuffering();
            // Streamed so the body is hashed without a second in-memory copy; model binding then re-reads the buffered stream.
            var hash = await SHA256.HashDataAsync(request.Body, http.RequestAborted);
            request.Body.Position = 0;
            http.Items[RequestHashItem] = Convert.ToHexString(hash);
        }

        await next();
    }

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
        var hash = http.Items[RequestHashItem] as string
            ?? throw new InvalidOperationException("The request body hash is set by the resource filter phase of IdempotencyFilter.");

        var existing = Decide(http, await store.GetAsync(key, ct), hash);
        if (existing is not null)
        {
            context.Result = existing;
            return;
        }

        if (!await store.TryBeginAsync(key, hash, PendingTtl, ct))
        {
            // Lost the claim to a concurrent request; judge it the same way.
            context.Result = Decide(http, await store.GetAsync(key, ct), hash) ?? InProgress(http);
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
        await store.CompleteAsync(key, hash, new IdempotentResponse(result.StatusCode.Value, location, body), CompletedTtl, CancellationToken.None);
    }

    /// <summary>The result to short-circuit with for a known key, or null when the key is free.</summary>
    private static IActionResult? Decide(HttpContext http, IdempotencyEntry? entry, string hash)
    {
        if (entry is null)
        {
            return null;
        }

        if (entry.RequestHash is not null && !string.Equals(entry.RequestHash, hash, StringComparison.Ordinal))
        {
            return Problem(http, StatusCodes.Status422UnprocessableEntity, "Idempotency-Key reused",
                "This Idempotency-Key was used with a different request body.");
        }

        if (entry.Response is not { } completed)
        {
            return InProgress(http);
        }

        if (completed.Location is not null)
        {
            http.Response.Headers.Location = completed.Location;
        }

        return new ContentResult
        {
            StatusCode = completed.Status,
            Content = completed.Body,
            ContentType = "application/json; charset=utf-8",
        };
    }

    private static ObjectResult InProgress(HttpContext http) =>
        Problem(http, StatusCodes.Status409Conflict, "Request in progress",
            "A request with this Idempotency-Key is already being processed.");

    private static ObjectResult Problem(HttpContext http, int status, string title, string detail)
    {
        var factory = http.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(http, status, title, detail: detail);
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
