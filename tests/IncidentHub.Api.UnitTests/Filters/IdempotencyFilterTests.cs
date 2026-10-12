using System.Text;
using FluentAssertions;
using IncidentHub.Api.Auth;
using IncidentHub.Api.Filters;
using IncidentHub.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace IncidentHub.Api.UnitTests.Filters;

public sealed class IdempotencyFilterTests
{
    private readonly IIdempotencyStore _store = Substitute.For<IIdempotencyStore>();
    private readonly IdempotencyFilter _filter;

    public IdempotencyFilterTests() =>
        _filter = new IdempotencyFilter(_store, Options.Create(new Microsoft.AspNetCore.Mvc.JsonOptions()));

    private static DefaultHttpContext NewHttp(string body)
    {
        var factory = Substitute.For<ProblemDetailsFactory>();
        factory.CreateProblemDetails(default!, default, default, default, default, default)
            .ReturnsForAnyArgs(call => new ProblemDetails { Status = call.ArgAt<int?>(1), Title = call.ArgAt<string?>(2) });

        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(factory).BuildServiceProvider(),
        };
        http.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return http;
    }

    private async Task<ActionExecutingContext> RunAsync(DefaultHttpContext http)
    {
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());

        await _filter.OnResourceExecutionAsync(
            new ResourceExecutingContext(action, [], []),
            () => Task.FromResult(new ResourceExecutedContext(action, [])));

        var context = new ActionExecutingContext(action, [], new Dictionary<string, object?>(), new object());
        await _filter.OnActionExecutionAsync(
            context,
            () => Task.FromResult(new ActionExecutedContext(action, [], new object())));
        return context;
    }

    private static string HashOf(DefaultHttpContext http) => (string)http.Items[IdempotencyFilter.RequestHashItem]!;

    [Fact]
    public async Task OnActionExecutionAsync_SameKeyDifferentBody_Returns422()
    {
        var http = NewHttp("{\"a\":1}");
        _store.GetAsync(default!, default).ReturnsForAnyArgs(new IdempotencyEntry("OTHERHASH", new IdempotentResponse(201, "/x", "{}")));

        var context = await RunAsync(http);

        context.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        await _store.DidNotReceiveWithAnyArgs().TryBeginAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task OnActionExecutionAsync_SameKeySameBody_ReplaysResponse()
    {
        var http = NewHttp("{\"a\":1}");
        var hash = await HashAsync("{\"a\":1}");
        _store.GetAsync(default!, default).ReturnsForAnyArgs(new IdempotencyEntry(hash, new IdempotentResponse(201, "/api/v1/x/1", "{\"id\":1}")));

        var context = await RunAsync(http);

        var result = context.Result.Should().BeOfType<ContentResult>().Subject;
        result.StatusCode.Should().Be(201);
        result.Content.Should().Be("{\"id\":1}");
        http.Response.Headers.Location.ToString().Should().Be("/api/v1/x/1");
    }

    [Fact]
    public async Task OnActionExecutionAsync_SameKeyStillPending_Returns409()
    {
        var http = NewHttp("{\"a\":1}");
        var hash = await HashAsync("{\"a\":1}");
        _store.GetAsync(default!, default).ReturnsForAnyArgs(new IdempotencyEntry(hash, null));

        var context = await RunAsync(http);

        context.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task OnActionExecutionAsync_LegacyEntryWithoutHash_IsTreatedAsSameBody()
    {
        var http = NewHttp("{\"a\":1}");
        _store.GetAsync(default!, default).ReturnsForAnyArgs(new IdempotencyEntry(null, new IdempotentResponse(201, null, "{}")));

        var context = await RunAsync(http);

        context.Result.Should().BeOfType<ContentResult>();
    }

    [Fact]
    public async Task OnResourceExecutionAsync_RewindsBodyAfterHashing()
    {
        var http = NewHttp("{\"a\":1}");
        _store.GetAsync(default!, default).ReturnsForAnyArgs((IdempotencyEntry?)null);
        _store.TryBeginAsync(default!, default!, default, default).ReturnsForAnyArgs(true);

        await RunAsync(http);

        http.Request.Body.Position.Should().Be(0);
        HashOf(http).Should().NotBeNullOrEmpty();
    }

    private static DefaultHttpContext WithLimit(DefaultHttpContext http, long limit)
    {
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new RequestSizeLimitAttribute(limit)), "test"));
        return http;
    }

    private async Task<Exception?> RunResourceAsync(DefaultHttpContext http)
    {
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        try
        {
            await _filter.OnResourceExecutionAsync(
                new ResourceExecutingContext(action, [], []),
                () => Task.FromResult(new ResourceExecutedContext(action, [])));
            return null;
        }
        catch (BadHttpRequestException ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task OnResourceExecutionAsync_ContentLengthOverEndpointLimit_Throws413BeforeReading()
    {
        var http = WithLimit(NewHttp("{\"a\":\"0123456789\"}"), limit: 5);
        http.Request.ContentLength = 18;

        var error = await RunResourceAsync(http);

        error.Should().BeOfType<BadHttpRequestException>().Which.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        http.Items.Should().NotContainKey(IdempotencyFilter.RequestHashItem);
    }

    [Fact]
    public async Task OnResourceExecutionAsync_EndpointLimit_IsAppliedToTheServerFeatureBeforeReading()
    {
        var http = WithLimit(NewHttp("{\"a\":1}"), limit: 1234);
        var feature = Substitute.For<IHttpMaxRequestBodySizeFeature>();
        feature.IsReadOnly.Returns(false);
        http.Features.Set(feature);

        await RunResourceAsync(http);

        feature.MaxRequestBodySize.Should().Be(1234);
    }

    [Fact]
    public async Task OnResourceExecutionAsync_NoEndpointLimit_UsesOneMebibyteDefault()
    {
        var http = NewHttp("{\"a\":1}");
        var feature = Substitute.For<IHttpMaxRequestBodySizeFeature>();
        feature.IsReadOnly.Returns(false);
        http.Features.Set(feature);
        http.Request.ContentLength = 1024 * 1024 + 1;

        var error = await RunResourceAsync(http);

        feature.MaxRequestBodySize.Should().Be(1024 * 1024);
        error.Should().BeOfType<BadHttpRequestException>().Which.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
    }

    [Fact]
    public async Task OnResourceExecutionAsync_NoIdempotencyKey_DoesNotReadTheBody()
    {
        var http = WithLimit(NewHttp("{\"a\":1}"), limit: 1);
        http.Request.Headers.Remove("Idempotency-Key");
        http.Request.ContentLength = 100;

        var error = await RunResourceAsync(http);

        error.Should().BeNull();
        http.Items.Should().NotContainKey(IdempotencyFilter.RequestHashItem);
    }

    private static async Task<string> HashAsync(string body)
    {
        var http = NewHttp(body);
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var filter = new IdempotencyFilter(Substitute.For<IIdempotencyStore>(), Options.Create(new Microsoft.AspNetCore.Mvc.JsonOptions()));
        await filter.OnResourceExecutionAsync(
            new ResourceExecutingContext(action, [], []),
            () => Task.FromResult(new ResourceExecutedContext(action, [])));
        return HashOf(http);
    }
}
