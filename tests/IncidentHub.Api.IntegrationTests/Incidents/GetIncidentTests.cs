using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;

namespace IncidentHub.Api.IntegrationTests.Incidents;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class GetIncidentTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    private static string NewOid() => Guid.NewGuid().ToString();

    private static string Route(string number) => $"/api/v1/incidents/{number}";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(HttpClient Client, Guid AppId, Guid UserId)> MemberAsync(UserRole role = UserRole.Responder)
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        return (factory.CreateClient().AsUser(factory, oid, role), appId, await factory.EnsureUserAsync(oid));
    }

    [Fact]
    public async Task Get_ByNumber_ReturnsDetailWithETag()
    {
        var (client, appId, _) = await MemberAsync();
        var (id, number) = await _seeder.AddIncidentAsync(appId, "Checkout fails after payment");

        var response = await client.GetAsync(Route(number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("id").GetGuid().Should().Be(id);
        body.GetProperty("number").GetString().Should().Be(number);
        body.GetProperty("title").GetString().Should().Be("Checkout fails after payment");
        body.GetProperty("description").GetString().Should().Contain("error page");
        body.GetProperty("status").GetString().Should().Be("New");
        body.GetProperty("source").GetString().Should().Be("Manual");
        body.GetProperty("duplicateOf").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("commentCount").GetInt32().Should().Be(0);
        body.GetProperty("attachments").GetArrayLength().Should().Be(0);
        body.GetProperty("application").GetProperty("id").GetGuid().Should().Be(appId);
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.Tag.Should().Be($"\"{body.GetProperty("rowVersion").GetString()}\"");
    }

    [Fact]
    public async Task Get_NumberIsCaseInsensitive()
    {
        var (client, appId, _) = await MemberAsync();
        var (id, number) = await _seeder.AddIncidentAsync(appId, "Lowercase lookup");

        var response = await client.GetAsync(Route(number.ToLowerInvariant()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("id").GetGuid().Should().Be(id);
    }

    [Fact]
    public async Task Get_ClosedDuplicate_ReturnsDuplicateOf()
    {
        var (client, appId, _) = await MemberAsync();
        var (originalId, originalNumber) = await _seeder.AddIncidentAsync(appId, "The original incident");
        var (_, duplicateNumber) = await _seeder.AddIncidentAsync(
            appId,
            "The duplicate report",
            configure: (inc, actor, at) => inc.TransitionTo(IncidentStatus.Closed, actor, null, at, CloseReason.Duplicate, originalId));

        var body = await JsonAsync(await client.GetAsync(Route(duplicateNumber)));

        body.GetProperty("status").GetString().Should().Be("Closed");
        body.GetProperty("closeReason").GetString().Should().Be("Duplicate");
        body.GetProperty("duplicateOf").GetProperty("id").GetGuid().Should().Be(originalId);
        body.GetProperty("duplicateOf").GetProperty("number").GetString().Should().Be(originalNumber);
        body.GetProperty("closedAt").ValueKind.Should().Be(JsonValueKind.String);
        body.GetProperty("allowedTransitions").GetArrayLength().Should().Be(0);
        body.GetProperty("canEdit").GetBoolean().Should().BeFalse();
        body.GetProperty("canAssign").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("INC-999999999")]
    [InlineData("INC-0")]
    [InlineData("not-a-number")]
    [InlineData("1001")]
    public async Task Get_UnknownOrMalformedNumber_Returns404(string number)
    {
        var (client, _, _) = await MemberAsync();

        var response = await client.GetAsync(Route(number));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_NonMember_Returns404WithSameBodyAsUnknown()
    {
        var foreignApp = await factory.CreateAppAsync();
        var (_, number) = await _seeder.AddIncidentAsync(foreignApp, "Hidden incident");
        var (client, _, _) = await MemberAsync();
        var admin = factory.CreateClient().AsUser(factory, NewOid(), UserRole.Admin);

        var hidden = await client.GetAsync(Route(number));
        var unknown = await client.GetAsync(Route("INC-999999999"));
        var adminHidden = await admin.GetAsync(Route(number));

        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        adminHidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await JsonAsync(hidden)).GetProperty("detail").GetString()
            .Should().Be((await JsonAsync(unknown)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Get_AllowedTransitions_ResponderAndAssigneeAndReporterAndStranger()
    {
        var (responderClient, appId, _) = await MemberAsync(UserRole.Responder);

        var assigneeOid = NewOid();
        await factory.AddToTeamAsync(assigneeOid, appId);
        var assigneeId = await factory.EnsureUserAsync(assigneeOid);
        var assigneeClient = factory.CreateClient().AsUser(factory, assigneeOid, UserRole.Reporter);

        var reporterOid = NewOid();
        await factory.AddToTeamAsync(reporterOid, appId);
        var reporterId = await factory.EnsureUserAsync(reporterOid);
        var reporterClient = factory.CreateClient().AsUser(factory, reporterOid, UserRole.Reporter);

        var strangerOid = NewOid();
        await factory.AddToTeamAsync(strangerOid, appId);
        var strangerClient = factory.CreateClient().AsUser(factory, strangerOid, UserRole.Reporter);

        var (_, inProgress) = await _seeder.AddIncidentAsync(
            appId,
            "In progress incident",
            reporterId: reporterId,
            configure: (inc, actor, at) =>
            {
                inc.Assign(assigneeId, null, actor, at);
                inc.TransitionTo(IncidentStatus.Triaged, actor, null, at);
                inc.TransitionTo(IncidentStatus.InProgress, actor, null, at);
            });

        (await TransitionsAsync(responderClient, inProgress)).Should().Equal("Resolved", "OnHold");
        (await TransitionsAsync(assigneeClient, inProgress)).Should().Equal("Resolved", "OnHold");
        (await TransitionsAsync(reporterClient, inProgress)).Should().BeEmpty();
        (await TransitionsAsync(strangerClient, inProgress)).Should().BeEmpty();
    }

    [Fact]
    public async Task Get_Flags_FollowRoleAndRelation()
    {
        var (responder, appId, _) = await MemberAsync(UserRole.Responder);
        var reporterOid = NewOid();
        await factory.AddToTeamAsync(reporterOid, appId);
        var reporterId = await factory.EnsureUserAsync(reporterOid);
        var reporter = factory.CreateClient().AsUser(factory, reporterOid, UserRole.Reporter);
        var otherOid = NewOid();
        await factory.AddToTeamAsync(otherOid, appId);
        var other = factory.CreateClient().AsUser(factory, otherOid, UserRole.Reporter);
        var (_, number) = await _seeder.AddIncidentAsync(appId, "Flags incident", reporterId: reporterId);

        var asResponder = await JsonAsync(await responder.GetAsync(Route(number)));
        var asReporter = await JsonAsync(await reporter.GetAsync(Route(number)));
        var asOther = await JsonAsync(await other.GetAsync(Route(number)));

        asResponder.GetProperty("canEdit").GetBoolean().Should().BeTrue();
        asResponder.GetProperty("canAssign").GetBoolean().Should().BeTrue();
        asReporter.GetProperty("canEdit").GetBoolean().Should().BeTrue();
        asReporter.GetProperty("canAssign").GetBoolean().Should().BeFalse();
        asOther.GetProperty("canEdit").GetBoolean().Should().BeFalse();
        asOther.GetProperty("canAssign").GetBoolean().Should().BeFalse();
        asResponder.GetProperty("allowedTransitions").EnumerateArray().Select(e => e.GetString()).Should().Equal("Triaged", "Closed");
    }

    [Fact]
    public async Task Get_ReporterOnlyUser_SeesTeamIncident()
    {
        var (client, appId, _) = await MemberAsync(UserRole.Reporter);
        var (_, number) = await _seeder.AddIncidentAsync(appId, "A colleague's incident");

        var response = await client.GetAsync(Route(number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_SimilarRouteStillWins()
    {
        var (client, appId, _) = await MemberAsync();

        var response = await client.GetAsync($"/api/v1/incidents/similar?applicationId={appId}&title=ab");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(response)).ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Get_NoToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(Route("INC-1001"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_NoRole_Returns403()
    {
        var client = factory.CreateClient().AsUser(factory, NewOid());

        var response = await client.GetAsync(Route("INC-1001"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<string?[]> TransitionsAsync(HttpClient client, string number)
    {
        var body = await JsonAsync(await client.GetAsync(Route(number)));
        return body.GetProperty("allowedTransitions").EnumerateArray().Select(e => e.GetString()).ToArray();
    }
}
