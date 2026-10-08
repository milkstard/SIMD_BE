using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace IncidentHub.Api.Auth;

/// <summary>Turns authorization failures into RFC 7807 responses. Challenges go through the JWT <c>OnChallenge</c> event.</summary>
public sealed class ProblemDetailsAuthorizationResultHandler(IProblemDetailsService problemDetails)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Forbidden",
                    Detail = "You do not have permission to perform this action.",
                },
            });
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
