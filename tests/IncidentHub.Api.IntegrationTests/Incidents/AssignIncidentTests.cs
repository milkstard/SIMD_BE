using System.Net;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Domain.Users;
using static IncidentHub.Api.IntegrationTests.Incidents.IncidentWriteHelpers;

namespace IncidentHub.Api.IntegrationTests.Incidents;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AssignIncidentTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    private static string NewOid() => Guid.NewGuid().ToString();

    private sealed record Scenario(HttpClient Client, Guid AppId, Guid Id, string Number, Guid CallerId);

    private async Task<Scenario> SeedAsync(UserRole role = UserRole.Responder, bool closed = false)
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        var callerId = await factory.EnsureUserAsync(oid);
        var (id, number) = await _seeder.AddIncidentAsync(
            appId,
            $"Checkout fails {Guid.NewGuid():N}",
            reporterId: role == UserRole.Reporter ? callerId : null,
            configure: closed ? (incident, actor, at) => incident.TransitionTo(IncidentStatus.Closed, actor, null, at, CloseReason.Invalid) : null);
        return new Scenario(factory.CreateClient().AsUser(factory, oid, role), appId, id, number, callerId);
    }

    /// <summary>A second user who belongs to the application's owning team.</summary>
    private async Task<Guid> TeammateAsync(Guid appId)
    {
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        return await factory.EnsureUserAsync(oid);
    }

    [Fact]
    public async Task Assign_UserAndTeam_Returns200WithHistory()
    {
        var s = await SeedAsync();
        var teammate = await TeammateAsync(s.AppId);
        var otherTeam = await factory.CreateTeamAsync();
        var etag = await ETagAsync(s.Client, s.Number);

        var response = await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = teammate, teamId = otherTeam }, etag);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("assignee").GetProperty("id").GetGuid().Should().Be(teammate);
        body.GetProperty("assignedTeam").GetProperty("id").GetGuid().Should().Be(otherTeam);
        body.GetProperty("rowVersion").GetString().Should().NotBe(etag.Trim('"'));
        response.Headers.ETag!.Tag.Should().Be($"\"{body.GetProperty("rowVersion").GetString()}\"");
        (await HistoryAsync(factory, s.Id)).Select(h => h.Field).Should().Contain(["AssigneeId", "AssignedTeamId"]);
    }

    [Fact]
    public async Task Assign_Unassign_ClearsFields()
    {
        var s = await SeedAsync();
        var teammate = await TeammateAsync(s.AppId);
        var team = await factory.CreateTeamAsync();
        (await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = teammate, teamId = team }, await ETagAsync(s.Client, s.Number)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = (Guid?)null, teamId = (Guid?)null }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("assignee").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("assignedTeam").ValueKind.Should().Be(JsonValueKind.Null);
        (await HistoryAsync(factory, s.Id)).Count(h => h.Field is "AssigneeId" or "AssignedTeamId").Should().Be(4);
    }

    [Fact]
    public async Task Assign_SameValues_NoHistoryAndSameETag()
    {
        var s = await SeedAsync();
        var teammate = await TeammateAsync(s.AppId);
        (await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = teammate }, await ETagAsync(s.Client, s.Number)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = await ETagAsync(s.Client, s.Number);
        var historyBefore = (await HistoryAsync(factory, s.Id)).Count;
        var outboxBefore = (await OutboxTypesAsync(factory, s.Id)).Count;

        var response = await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = teammate }, etag);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.Should().Be(etag);
        (await HistoryAsync(factory, s.Id)).Should().HaveCount(historyBefore);
        (await OutboxTypesAsync(factory, s.Id)).Should().HaveCount(outboxBefore);
    }

    [Fact]
    public async Task Assign_ClosedIncident_Returns409()
    {
        var s = await SeedAsync(closed: true);
        var teammate = await TeammateAsync(s.AppId);

        var response = await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = teammate }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Assign_ReporterRole_Returns403()
    {
        var s = await SeedAsync(UserRole.Reporter);

        var response = await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = s.CallerId }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Assign_AssigneeNotOnTeam_Returns422()
    {
        var s = await SeedAsync();
        var outsider = await factory.EnsureUserAsync(NewOid());

        var response = await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = outsider }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "assigneeId");
    }

    [Fact]
    public async Task Assign_UnknownAssignee_Returns422()
    {
        var s = await SeedAsync();

        var response = await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = Guid.NewGuid() }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "assigneeId");
    }

    [Fact]
    public async Task Assign_UnknownTeam_Returns422()
    {
        var s = await SeedAsync();

        var response = await PostAsync(s.Client, s.Id, "assignment", new { teamId = Guid.NewGuid() }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "teamId");
    }

    [Fact]
    public async Task Assign_EmptyGuidAssignee_Returns422()
    {
        var s = await SeedAsync();

        var response = await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = Guid.Empty }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "assigneeId");
    }

    [Fact]
    public async Task Assign_StaleIfMatch_Returns409AndChangesNothing()
    {
        var s = await SeedAsync();
        var teammate = await TeammateAsync(s.AppId);
        var stale = await ETagAsync(s.Client, s.Number);
        (await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = s.CallerId }, stale)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = teammate }, stale);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(await s.Client.GetAsync(Route(s.Number)))).GetProperty("assignee").GetProperty("id").GetGuid().Should().Be(s.CallerId);
    }

    [Fact]
    public async Task Assign_NoIfMatch_Returns428()
    {
        var s = await SeedAsync();

        var response = await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = s.CallerId }, null);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task Assign_NonMember_Returns404AndChangesNothing()
    {
        var s = await SeedAsync();
        var etag = await ETagAsync(s.Client, s.Number);
        var admin = factory.CreateClient().AsUser(factory, NewOid(), UserRole.Admin);

        var response = await PostAsync(admin, s.Id, "assignment", new { assigneeId = s.CallerId }, etag);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await JsonAsync(await s.Client.GetAsync(Route(s.Number)))).GetProperty("assignee").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Assign_NoToken_Returns401()
    {
        var response = await PostAsync(factory.CreateClient(), Guid.NewGuid(), "assignment", new { }, "\"AAAA\"");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Assign_WritesOutboxRow()
    {
        var s = await SeedAsync();

        (await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = s.CallerId }, await ETagAsync(s.Client, s.Number)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await OutboxTypesAsync(factory, s.Id)).Should().Contain(nameof(IncidentAssigned));
    }

    private static async Task AssertErrorKeyAsync(HttpResponseMessage response, string key)
    {
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("errors").TryGetProperty(key, out _).Should().BeTrue($"errors should name {key}");
    }
}
