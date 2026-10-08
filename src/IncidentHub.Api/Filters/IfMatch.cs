using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace IncidentHub.Api.Filters;

/// <summary>
/// Requires an <c>If-Match</c> header holding the quoted base64 RowVersion returned in <c>ETag</c>.
/// Missing gives 428, malformed gives 400. Read the decoded bytes with <see cref="IfMatchExtensions.GetIfMatchRowVersion"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireIfMatchAttribute : ActionFilterAttribute
{
    internal const string ItemKey = "IfMatch.RowVersion";

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var http = context.HttpContext;
        if (!http.Request.Headers.TryGetValue("If-Match", out var header) || string.IsNullOrWhiteSpace(header))
        {
            context.Result = Problem(http, StatusCodes.Status428PreconditionRequired, "Precondition required",
                "This request requires an If-Match header with the current ETag.");
            return;
        }

        var raw = header.ToString().Trim();
        if (raw.StartsWith("W/", StringComparison.Ordinal))
        {
            raw = raw[2..];
        }

        raw = raw.Trim('"');
        var buffer = new byte[raw.Length];
        if (raw.Length == 0 || raw == "*" || !Convert.TryFromBase64String(raw, buffer, out var written))
        {
            context.Result = Problem(http, StatusCodes.Status400BadRequest, "Malformed If-Match",
                "If-Match must contain the quoted ETag returned by the server.");
            return;
        }

        http.Items[ItemKey] = buffer[..written];
    }

    private static ObjectResult Problem(HttpContext http, int status, string title, string detail)
    {
        var factory = http.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(http, status, title, detail: detail);
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}

public static class IfMatchExtensions
{
    /// <summary>The RowVersion bytes parsed by <see cref="RequireIfMatchAttribute"/>.</summary>
    public static byte[] GetIfMatchRowVersion(this HttpContext context) =>
        context.Items[RequireIfMatchAttribute.ItemKey] as byte[]
        ?? throw new InvalidOperationException("The action is missing [RequireIfMatch].");
}
