using FluentAssertions;
using IncidentHub.Api.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace IncidentHub.Api.UnitTests.Filters;

public sealed class RequireIfMatchTests
{
    private static ActionExecutingContext CreateContext(string? ifMatch)
    {
        var factory = Substitute.For<ProblemDetailsFactory>();
        factory.CreateProblemDetails(default!, default, default, default, default, default)
            .ReturnsForAnyArgs(call => new ProblemDetails { Status = call.ArgAt<int?>(1), Title = call.ArgAt<string?>(2) });

        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(factory).BuildServiceProvider(),
        };
        if (ifMatch is not null)
        {
            http.Request.Headers.IfMatch = ifMatch;
        }

        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(action, [], new Dictionary<string, object?>(), new object());
    }

    [Fact]
    public void OnActionExecuting_MissingHeader_Returns428()
    {
        var context = CreateContext(null);

        new RequireIfMatchAttribute().OnActionExecuting(context);

        context.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status428PreconditionRequired);
    }

    [Theory]
    [InlineData("\"***not base64***\"")]
    [InlineData("*")]
    [InlineData("\"\"")]
    public void OnActionExecuting_MalformedHeader_Returns400(string header)
    {
        var context = CreateContext(header);

        new RequireIfMatchAttribute().OnActionExecuting(context);

        context.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Theory]
    [InlineData("\"AAAAAAAAB9E=\"")]
    [InlineData("W/\"AAAAAAAAB9E=\"")]
    [InlineData("AAAAAAAAB9E=")]
    public void OnActionExecuting_ValidHeader_StoresDecodedBytes(string header)
    {
        var context = CreateContext(header);

        new RequireIfMatchAttribute().OnActionExecuting(context);

        context.Result.Should().BeNull();
        context.HttpContext.GetIfMatchRowVersion().Should().Equal(Convert.FromBase64String("AAAAAAAAB9E="));
    }
}
