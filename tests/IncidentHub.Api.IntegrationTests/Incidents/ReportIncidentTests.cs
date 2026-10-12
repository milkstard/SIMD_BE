using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Incidents;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReportIncidentTests(ApiFactory factory)
{
    private const string Route = "/api/v1/incidents";
    private const string Unavailable = "Application not found or not available to you.";

    private static string NewOid() => Guid.NewGuid().ToString();

    private static string NewTitle() => $"Checkout fails {Guid.NewGuid():N}";

    private static object Body(Guid appId, string? title = null, string severity = "High", string environment = "Production", string description = "<p>Error page after paying.</p>", string? steps = "1. Pay") =>
        new { applicationId = appId, environment, severity, title = title ?? NewTitle(), description, stepsToReproduce = steps };

    private HttpClient As(string oid, params UserRole[] roles) => factory.CreateClient().AsUser(factory, oid, roles);

    /// <summary>Creates an application and a client for a member of its team holding <paramref name="role"/>.</summary>
    private async Task<(HttpClient Client, string Oid, Guid AppId)> MemberAsync(UserRole role = UserRole.Responder)
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        return (As(oid, role), oid, appId);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, int status)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await JsonAsync(response);
        body.GetProperty("status").GetInt32().Should().Be(status);
        body.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        return body;
    }

    private static string[] ErrorKeys(JsonElement problem) => problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToArray();

    private static int NumberOf(JsonElement incident) => int.Parse(incident.GetProperty("number").GetString()!["INC-".Length..]);

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> read)
    {
        using var scope = factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<int> CountByTitleAsync(string title) => WithDbAsync(db => db.Incidents.CountAsync(i => i.Title == title));

    [Fact]
    public async Task Report_Valid_Returns201WithLocationETagAndDetail()
    {
        var (client, oid, appId) = await MemberAsync();

        var response = await PostAsync(client, Body(appId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var incident = await JsonAsync(response);
        var number = incident.GetProperty("number").GetString()!;
        number.Should().MatchRegex("^INC-\\d+$");
        response.Headers.Location!.ToString().Should().Be($"/api/v1/incidents/{number}");
        response.Headers.ETag!.Tag.Should().Be($"\"{incident.GetProperty("rowVersion").GetString()}\"");
        incident.GetProperty("status").GetString().Should().Be("New");
        incident.GetProperty("source").GetString().Should().Be("Manual");
        incident.GetProperty("severity").GetString().Should().Be("High");
        incident.GetProperty("application").GetProperty("id").GetGuid().Should().Be(appId);
        incident.GetProperty("reporter").GetProperty("id").GetGuid().Should().Be(await factory.EnsureUserAsync(oid));
        incident.GetProperty("assignee").ValueKind.Should().Be(JsonValueKind.Null);
        incident.GetProperty("commentCount").GetInt32().Should().Be(0);
        incident.GetProperty("attachments").GetArrayLength().Should().Be(0);
        incident.GetProperty("allowedTransitions").EnumerateArray().Select(e => e.GetString()).Should().Equal("Triaged", "Closed");
        incident.GetProperty("canEdit").GetBoolean().Should().BeTrue();
        incident.GetProperty("canAssign").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Report_ReporterRole_Returns201WithNoTransitionsAndNoAssign()
    {
        var (client, _, appId) = await MemberAsync(UserRole.Reporter);

        var response = await PostAsync(client, Body(appId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var incident = await JsonAsync(response);
        incident.GetProperty("allowedTransitions").GetArrayLength().Should().Be(0);
        incident.GetProperty("canEdit").GetBoolean().Should().BeTrue();
        incident.GetProperty("canAssign").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("Critical", 15, 240)]
    [InlineData("High", 60, 480)]
    [InlineData("Medium", 240, 4320)]
    [InlineData("Low", 480, 7200)]
    public async Task Report_Valid_SetsSlaDueTimesForSeverity(string severity, int ackMinutes, int resolveMinutes)
    {
        var (client, _, appId) = await MemberAsync();

        var response = await PostAsync(client, Body(appId, severity: severity));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var incident = await JsonAsync(response);
        var createdAt = incident.GetProperty("createdAt").GetDateTimeOffset();
        incident.GetProperty("ackDueAt").GetDateTimeOffset().Should().Be(createdAt.AddMinutes(ackMinutes));
        incident.GetProperty("resolveDueAt").GetDateTimeOffset().Should().Be(createdAt.AddMinutes(resolveMinutes));

        var stored = await WithDbAsync(db => db.Incidents.AsNoTracking().SingleAsync(i => i.Id == incident.GetProperty("id").GetGuid()));
        stored.AckDueAt.Should().Be(createdAt.AddMinutes(ackMinutes));
        stored.ResolveDueAt.Should().Be(createdAt.AddMinutes(resolveMinutes));
    }

    [Fact]
    public async Task Report_Valid_WritesIncidentHistoryAndOutboxRows()
    {
        var (client, oid, appId) = await MemberAsync();

        var incident = await JsonAsync(await PostAsync(client, Body(appId)));
        var id = incident.GetProperty("id").GetGuid();
        var userId = await factory.EnsureUserAsync(oid);

        var history = await WithDbAsync(db => db.IncidentHistory.AsNoTracking().Where(h => h.IncidentId == id).ToListAsync());
        history.Should().ContainSingle();
        history[0].Field.Should().Be("Status");
        history[0].OldValue.Should().BeNull();
        history[0].NewValue.Should().Be("New");
        history[0].ActorId.Should().Be(userId);

        var outbox = await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Payload.Contains(id.ToString())).ToListAsync());
        outbox.Should().ContainSingle();
        outbox[0].Type.Should().Be("IncidentReported");
        outbox[0].ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task Report_InvalidPayload_Returns422WithFieldErrors()
    {
        var (client, _, appId) = await MemberAsync();
        var body = new
        {
            applicationId = appId,
            environment = "Production",
            severity = "High",
            title = "abc",
            description = new string('a', 20_001),
            attachmentIds = new[] { Guid.NewGuid() },
        };

        var response = await PostAsync(client, body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        ErrorKeys(await ProblemAsync(response, 422)).Should().BeEquivalentTo("title", "description", "attachmentIds");
    }

    [Fact]
    public async Task Report_EmptyBody_Returns422ForEveryRequiredField()
    {
        var (client, _, _) = await MemberAsync();

        var response = await PostAsync(client, new { });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        ErrorKeys(await ProblemAsync(response, 422)).Should()
            .BeEquivalentTo("applicationId", "environment", "severity", "title", "description");
    }

    [Fact]
    public async Task Report_MalformedJson_Returns400()
    {
        var (client, _, _) = await MemberAsync();

        var response = await client.PostAsync(Route, new StringContent("{ not json", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Report_UnknownEnumName_Returns400()
    {
        var (client, _, appId) = await MemberAsync();

        var response = await PostAsync(client, Body(appId, severity: "Huge"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Report_BodyOverSizeLimitWithIdempotencyKey_Returns413AndCreatesNothing()
    {
        var (client, _, appId) = await MemberAsync();
        var title = NewTitle();
        var body = Body(appId, title, description: new string('a', 300 * 1024));

        var response = await PostAsync(client, body, Guid.NewGuid().ToString());

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await ProblemAsync(response, 413)).GetProperty("title").GetString().Should().Be("Request body too large");
        (await CountByTitleAsync(title)).Should().Be(0);
    }

    [Fact]
    public async Task Report_InvalidIdempotencyKey_Returns400()
    {
        var (client, _, appId) = await MemberAsync();

        var response = await PostAsync(client, Body(appId), key: "not-a-uuid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Report_SameIdempotencyKeyTwice_CreatesOneIncident()
    {
        var (client, _, appId) = await MemberAsync();
        var title = NewTitle();
        var body = Body(appId, title);
        var key = Guid.NewGuid().ToString();

        var first = await PostAsync(client, body, key);
        var second = await PostAsync(client, body, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Headers.Location.Should().Be(first.Headers.Location);
        (await JsonAsync(second)).GetProperty("id").GetGuid().Should().Be((await JsonAsync(first)).GetProperty("id").GetGuid());
        (await CountByTitleAsync(title)).Should().Be(1);
    }

    [Fact]
    public async Task Report_SameIdempotencyKeyConcurrent_CreatesOneIncident()
    {
        var (client, _, appId) = await MemberAsync();
        var title = NewTitle();
        var body = Body(appId, title);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(PostAsync(client, body, key), PostAsync(client, body, key));

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created || s == HttpStatusCode.Conflict);
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.Created);
        (await CountByTitleAsync(title)).Should().Be(1);
    }

    [Fact]
    public async Task Report_SameKeyDifferentBody_Returns422()
    {
        var (client, _, appId) = await MemberAsync();
        var key = Guid.NewGuid().ToString();
        var otherTitle = NewTitle();
        (await PostAsync(client, Body(appId), key)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await PostAsync(client, Body(appId, otherTitle), key);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProblemAsync(response, 422)).GetProperty("title").GetString().Should().Be("Idempotency-Key reused");
        (await CountByTitleAsync(otherTitle)).Should().Be(0);
    }

    [Fact]
    public async Task Report_ScriptInDescription_StoredSanitized()
    {
        var (client, _, appId) = await MemberAsync();
        var body = Body(
            appId,
            description: "<p>Hi</p><script>alert(1)</script><img src=\"x.png\" onerror=\"alert(2)\">",
            steps: "<p>Step</p><script>alert(3)</script>");

        var response = await PostAsync(client, body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await JsonAsync(response)).GetProperty("id").GetGuid();
        var stored = await WithDbAsync(db => db.Incidents.AsNoTracking().SingleAsync(i => i.Id == id));
        stored.Description.Should().Contain("<p>Hi</p>").And.NotContain("script").And.NotContain("onerror");
        stored.StepsToReproduce.Should().Contain("<p>Step</p>").And.NotContain("script");
    }

    [Fact]
    public async Task Report_DescriptionEmptyAfterSanitize_Returns422()
    {
        var (client, _, appId) = await MemberAsync();

        var response = await PostAsync(client, Body(appId, description: "<script>alert(1)</script>"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        ErrorKeys(await ProblemAsync(response, 422)).Should().Equal("description");
    }

    [Fact]
    public async Task Report_NonMember_Returns422()
    {
        var appId = await factory.CreateAppAsync();
        var title = NewTitle();

        var response = await PostAsync(As(NewOid(), UserRole.Responder), Body(appId, title));

        await AssertApplicationUnavailableAsync(response);
        (await CountByTitleAsync(title)).Should().Be(0);
    }

    [Fact]
    public async Task Report_AdminNotMember_Returns422()
    {
        var appId = await factory.CreateAppAsync();

        var response = await PostAsync(As(NewOid(), UserRole.Admin), Body(appId));

        await AssertApplicationUnavailableAsync(response);
    }

    [Fact]
    public async Task Report_UnknownApplication_Returns422()
    {
        var response = await PostAsync(As(NewOid(), UserRole.Responder), Body(Guid.NewGuid()));

        await AssertApplicationUnavailableAsync(response);
    }

    [Fact]
    public async Task Report_InactiveApplication_Returns422()
    {
        var (client, _, appId) = await MemberAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = await db.Applications.SingleAsync(a => a.Id == appId);
            app.Update(app.Name, app.Code, app.OwningTeamId, null, app.Environments, isActive: false);
            await db.SaveChangesAsync();
        }

        var response = await PostAsync(client, Body(appId));

        await AssertApplicationUnavailableAsync(response);
    }

    private static async Task AssertApplicationUnavailableAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await ProblemAsync(response, 422);
        problem.GetProperty("errors").GetProperty("applicationId")[0].GetString().Should().Be(Unavailable);
    }

    [Fact]
    public async Task Report_EnvironmentNotOffered_Returns422()
    {
        var (client, _, appId) = await MemberAsync();

        var response = await PostAsync(client, Body(appId, environment: "UAT"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        ErrorKeys(await ProblemAsync(response, 422)).Should().Equal("environment");
    }

    [Fact]
    public async Task Report_OpenDuplicate_Returns409WithExistingNumber()
    {
        var (client, _, appId) = await MemberAsync();
        var title = NewTitle();
        var first = await JsonAsync(await PostAsync(client, Body(appId, title)));

        var response = await PostAsync(client, Body(appId, $"  {title.ToUpperInvariant()}  "));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await ProblemAsync(response, 409);
        problem.GetProperty("title").GetString().Should().Be("Duplicate incident");
        problem.GetProperty("existingIncidentNumber").GetString().Should().Be(first.GetProperty("number").GetString());
    }

    [Fact]
    public async Task Report_DuplicateOfClosed_Returns201()
    {
        var (client, oid, appId) = await MemberAsync();
        var title = NewTitle();
        var first = await JsonAsync(await PostAsync(client, Body(appId, title)));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var incident = await db.Incidents.SingleAsync(i => i.Number == NumberOf(first));
            var responder = new Actor(await factory.EnsureUserAsync(oid), new HashSet<UserRole> { UserRole.Responder });
            incident.TransitionTo(IncidentStatus.Closed, responder, null, DateTimeOffset.UtcNow, CloseReason.Invalid);
            await db.SaveChangesAsync();
        }

        var response = await PostAsync(client, Body(appId, title));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        NumberOf(await JsonAsync(response)).Should().BeGreaterThan(NumberOf(first));
    }

    [Fact]
    public async Task Report_NoToken_Returns401()
    {
        var response = await PostAsync(factory.CreateClient(), Body(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Report_NoRole_Returns403()
    {
        var response = await PostAsync(As(NewOid()), Body(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
