using IncidentHub.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IncidentHub.Api.Filters;

/// <summary>Adds an <c>ETag</c> header to every response whose body is an <see cref="IVersionedDto"/>.</summary>
public sealed class ETagResultFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: IVersionedDto dto })
        {
            context.HttpContext.Response.Headers.ETag = $"\"{dto.RowVersion}\"";
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
