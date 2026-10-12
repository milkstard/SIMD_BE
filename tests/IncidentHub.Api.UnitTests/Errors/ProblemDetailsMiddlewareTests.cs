using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.Errors;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Domain.Incidents;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IncidentHub.Api.UnitTests.Errors;

public sealed class ProblemDetailsMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_InvalidTransition_Returns409()
    {
        var (status, body) = await Run(new InvalidTransitionException(IncidentStatus.New, IncidentStatus.Resolved));

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("type").GetString().Should().Be("https://incidenthub/errors/invalid-transition");
        body.GetProperty("detail").GetString().Should().Contain("New").And.Contain("Resolved");
    }

    [Fact]
    public async Task InvokeAsync_IncidentClosed_Returns409()
    {
        var (status, body) = await Run(new IncidentClosedException());

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("type").GetString().Should().Be("https://incidenthub/errors/invalid-transition");
    }

    [Fact]
    public async Task InvokeAsync_DuplicateIncident_Returns409()
    {
        var (status, body) = await Run(new DuplicateIncidentException());

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("detail").GetString().Should().Contain("open incident");
    }

    [Fact]
    public async Task InvokeAsync_DuplicateIncidentWithNumber_Returns409WithExistingNumber()
    {
        var (status, body) = await Run(new DuplicateIncidentException("INC-1042"));

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("title").GetString().Should().Be("Duplicate incident");
        body.GetProperty("existingIncidentNumber").GetString().Should().Be("INC-1042");
    }

    [Fact]
    public async Task InvokeAsync_DuplicateIncidentWithoutNumber_Returns409WithoutExtension()
    {
        var (_, body) = await Run(new DuplicateIncidentException());

        body.TryGetProperty("existingIncidentNumber", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(StatusCodes.Status413PayloadTooLarge)]
    [InlineData(StatusCodes.Status400BadRequest)]
    public async Task InvokeAsync_BadHttpRequest_KeepsServerStatus(int statusCode)
    {
        var (status, _) = await Run(new BadHttpRequestException("body", statusCode));

        status.Should().Be(statusCode);
    }

    [Fact]
    public void Report_Action_HasRequestSizeLimit()
    {
        var method = typeof(IncidentHub.Api.Controllers.IncidentsController).GetMethod("Report")!;

        method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute), inherit: false)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task InvokeAsync_TransitionNotPermitted_Returns403()
    {
        var (status, _) = await Run(new TransitionNotPermittedException(IncidentStatus.New, IncidentStatus.Triaged));

        status.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task InvokeAsync_TransitionRequirement_Returns422WithFieldErrors()
    {
        var (status, body) = await Run(new TransitionRequirementException("note", "A note is required when moving to Resolved."));

        status.Should().Be(StatusCodes.Status422UnprocessableEntity);
        body.GetProperty("errors").GetProperty("note")[0].GetString().Should().Be("A note is required when moving to Resolved.");
    }

    private static async Task<(int Status, JsonElement Body)> Run(Exception toThrow)
    {
        await using var provider = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Response.Body = new MemoryStream();
        var middleware = new ProblemDetailsMiddleware(
            _ => throw toThrow,
            provider.GetRequiredService<IProblemDetailsService>(),
            NullLogger<ProblemDetailsMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }
}
