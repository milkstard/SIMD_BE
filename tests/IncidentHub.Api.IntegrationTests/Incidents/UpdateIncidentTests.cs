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
public sealed class UpdateIncidentTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    private static string NewOid() => Guid.NewGuid().ToString();

    private sealed record Scenario(HttpClient Client, string Oid, Guid AppId, Guid Id, string Number, string Title, Guid CallerId);

    /// <summary>An open High incident; the caller is on the owning team. A Reporter-role caller is also the incident's reporter.</summary>
    private async Task<Scenario> SeedAsync(UserRole role = UserRole.Responder, bool closed = false)
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        var callerId = await factory.EnsureUserAsync(oid);
        var title = $"Checkout fails {Guid.NewGuid():N}";
        var (id, number) = await _seeder.AddIncidentAsync(
            appId,
            title,
            reporterId: role == UserRole.Reporter ? callerId : null,
            configure: closed ? (incident, actor, at) => incident.TransitionTo(IncidentStatus.Closed, actor, null, at, CloseReason.Invalid) : null);
        return new Scenario(factory.CreateClient().AsUser(factory, oid, role), oid, appId, id, number, title, callerId);
    }

    private static async Task AssertErrorKeyAsync(HttpResponseMessage response, string key)
    {
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("errors").TryGetProperty(key, out _).Should().BeTrue($"errors should name {key}");
    }

    [Fact]
    public async Task Patch_ResponderChangesTitle_Returns200WithHistory()
    {
        var s = await SeedAsync();
        var etag = await ETagAsync(s.Client, s.Number);

        var response = await PatchAsync(s.Client, s.Id, new { title = "Checkout fails after payment" }, etag);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("title").GetString().Should().Be("Checkout fails after payment");
        body.GetProperty("rowVersion").GetString().Should().NotBe(etag.Trim('"'));
        response.Headers.ETag!.Tag.Should().Be($"\"{body.GetProperty("rowVersion").GetString()}\"");
        var history = (await HistoryAsync(factory, s.Id)).Where(h => h.Field == "Title").ToList();
        history.Should().ContainSingle();
        history[0].OldValue.Should().Be(s.Title);
        history[0].NewValue.Should().Be("Checkout fails after payment");
    }

    [Fact]
    public async Task Patch_OnlySentFieldsChange()
    {
        var s = await SeedAsync();
        var before = await JsonAsync(await s.Client.GetAsync(Route(s.Number)));

        var response = await PatchAsync(s.Client, s.Id, new { priority = "P2" }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("priority").GetString().Should().Be("P2");
        body.GetProperty("title").GetString().Should().Be(before.GetProperty("title").GetString());
        body.GetProperty("description").GetString().Should().Be(before.GetProperty("description").GetString());
        body.GetProperty("severity").GetString().Should().Be(before.GetProperty("severity").GetString());
        body.GetProperty("ackDueAt").GetRawText().Should().Be(before.GetProperty("ackDueAt").GetRawText());
    }

    [Fact]
    public async Task Patch_ClearStepsAndPriority_SetsNull()
    {
        var s = await SeedAsync();
        (await PatchAsync(s.Client, s.Id, new { priority = "P1", stepsToReproduce = "1. Pay" }, await ETagAsync(s.Client, s.Number)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await PatchAsync(
            s.Client, s.Id, new { priority = (string?)null, stepsToReproduce = (string?)null }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("priority").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("stepsToReproduce").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Patch_SameValues_NoHistory()
    {
        var s = await SeedAsync();
        var etag = await ETagAsync(s.Client, s.Number);
        var historyBefore = (await HistoryAsync(factory, s.Id)).Count;
        var outboxBefore = (await OutboxTypesAsync(factory, s.Id)).Count;

        var response = await PatchAsync(s.Client, s.Id, new { title = s.Title }, etag);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.Should().Be(etag);
        (await HistoryAsync(factory, s.Id)).Should().HaveCount(historyBefore);
        (await OutboxTypesAsync(factory, s.Id)).Should().HaveCount(outboxBefore);
    }

    [Fact]
    public async Task Patch_SeverityWithReason_RecomputesDueTimes()
    {
        var s = await SeedAsync();

        var response = await PatchAsync(
            s.Client, s.Id, new { severity = "Critical", reason = "Payments are down for everyone" }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("severity").GetString().Should().Be("Critical");
        var changedAt = body.GetProperty("updatedAt").GetDateTimeOffset();
        // Critical policy in appsettings.json: ack 15 min, resolve 240 min, counted from the change.
        body.GetProperty("ackDueAt").GetDateTimeOffset().Should().Be(changedAt.AddMinutes(15));
        body.GetProperty("resolveDueAt").GetDateTimeOffset().Should().Be(changedAt.AddMinutes(240));
        var severity = (await HistoryAsync(factory, s.Id)).Single(h => h.Field == "Severity");
        severity.OldValue.Should().Be("High");
        severity.NewValue.Should().Be("Critical");
        severity.Note.Should().Be("Payments are down for everyone");
    }

    [Fact]
    public async Task Patch_SeverityWithoutReason_Returns422()
    {
        var s = await SeedAsync();

        var response = await PatchAsync(s.Client, s.Id, new { severity = "Critical" }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "reason");
    }

    [Fact]
    public async Task Patch_SeverityToSameValue_NoHistory()
    {
        var s = await SeedAsync();
        var etag = await ETagAsync(s.Client, s.Number);
        var historyBefore = (await HistoryAsync(factory, s.Id)).Count;

        var response = await PatchAsync(s.Client, s.Id, new { severity = "High", reason = "No change" }, etag);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.Should().Be(etag);
        (await HistoryAsync(factory, s.Id)).Should().HaveCount(historyBefore);
    }

    [Fact]
    public async Task Patch_ReporterEditsOwnTitle_Returns200()
    {
        var s = await SeedAsync(UserRole.Reporter);

        var response = await PatchAsync(s.Client, s.Id, new { title = "Checkout fails for me" }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("title").GetString().Should().Be("Checkout fails for me");
    }

    [Fact]
    public async Task Patch_ReporterChangesSeverity_Returns403()
    {
        var s = await SeedAsync(UserRole.Reporter);

        var response = await PatchAsync(
            s.Client, s.Id, new { severity = "Critical", reason = "Urgent" }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Patch_ReporterChangesPriority_Returns403()
    {
        var s = await SeedAsync(UserRole.Reporter);

        var response = await PatchAsync(s.Client, s.Id, new { priority = "P1" }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Patch_OtherUser_Returns403()
    {
        var s = await SeedAsync(UserRole.Reporter);
        var otherOid = NewOid();
        await factory.AddToTeamAsync(otherOid, s.AppId);
        var other = factory.CreateClient().AsUser(factory, otherOid, UserRole.Reporter);

        var response = await PatchAsync(other, s.Id, new { title = "Somebody else edits this" }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Patch_NonMember_Returns404()
    {
        var s = await SeedAsync();
        var etag = await ETagAsync(s.Client, s.Number);
        var admin = factory.CreateClient().AsUser(factory, NewOid(), UserRole.Admin);

        var response = await PatchAsync(admin, s.Id, new { title = "Admin from another team" }, etag);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await JsonAsync(await s.Client.GetAsync(Route(s.Number)))).GetProperty("title").GetString().Should().Be(s.Title);
    }

    [Fact]
    public async Task Patch_StaleIfMatch_Returns409()
    {
        var s = await SeedAsync();
        var stale = await ETagAsync(s.Client, s.Number);
        (await PatchAsync(s.Client, s.Id, new { title = "First edit wins" }, stale)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await PatchAsync(s.Client, s.Id, new { title = "Second edit is stale" }, stale);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(await s.Client.GetAsync(Route(s.Number)))).GetProperty("title").GetString().Should().Be("First edit wins");
    }

    [Fact]
    public async Task Patch_NoIfMatch_Returns428()
    {
        var s = await SeedAsync();

        var response = await PatchAsync(s.Client, s.Id, new { title = "No precondition" }, null);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task Patch_MalformedIfMatch_Returns400()
    {
        var s = await SeedAsync();

        var response = await PatchAsync(s.Client, s.Id, new { title = "Bad precondition" }, "\"!!not-base64!!\"");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Patch_ClosedIncident_Returns409()
    {
        var s = await SeedAsync(closed: true);

        var response = await PatchAsync(s.Client, s.Id, new { title = "Edit a closed incident" }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(response)).GetProperty("type").GetString().Should().EndWith("invalid-transition");
    }

    [Fact]
    public async Task Patch_TitleCollidesWithOpenIncident_Returns409WithExistingNumber()
    {
        var s = await SeedAsync();
        var otherTitle = $"Search is slow {Guid.NewGuid():N}";
        var (_, otherNumber) = await _seeder.AddIncidentAsync(s.AppId, otherTitle);

        var response = await PatchAsync(s.Client, s.Id, new { title = otherTitle }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(response)).GetProperty("existingIncidentNumber").GetString().Should().Be(otherNumber);
    }

    [Fact]
    public async Task Patch_MoveToAnotherApplication_Returns200()
    {
        var s = await SeedAsync();
        var target = await factory.CreateAppAsync();
        await factory.AddToTeamAsync(s.Oid, target);
        // The assignee stays as it was after the move.
        (await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = s.CallerId }, await ETagAsync(s.Client, s.Number)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await PatchAsync(s.Client, s.Id, new { applicationId = target }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("application").GetProperty("id").GetGuid().Should().Be(target);
        body.GetProperty("assignee").GetProperty("id").GetGuid().Should().Be(s.CallerId);
        (await HistoryAsync(factory, s.Id)).Should().Contain(h => h.Field == "ApplicationId");
    }

    [Fact]
    public async Task Patch_MoveToApplicationOfOtherTeam_Returns422()
    {
        var s = await SeedAsync();
        var foreign = await factory.CreateAppAsync();

        var response = await PatchAsync(s.Client, s.Id, new { applicationId = foreign }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "applicationId");
    }

    [Fact]
    public async Task Patch_EnvironmentNotOffered_Returns422()
    {
        var s = await SeedAsync();

        // The test application is only offered in Production.
        var response = await PatchAsync(s.Client, s.Id, new { environment = "UAT" }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "environment");
    }

    [Fact]
    public async Task Patch_ScriptInDescription_StoredSanitized()
    {
        var s = await SeedAsync();

        var response = await PatchAsync(
            s.Client,
            s.Id,
            new { description = "<p>Hello</p><script>alert(1)</script><img src=x onerror=alert(2)>", stepsToReproduce = "<b onclick=alert(3)>1. Pay</b>" },
            await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("description").GetString().Should().Contain("Hello").And.NotContain("script").And.NotContain("onerror");
        body.GetProperty("stepsToReproduce").GetString().Should().Contain("1. Pay").And.NotContain("onclick");
    }

    [Fact]
    public async Task Patch_EmptyBody_Returns422()
    {
        var s = await SeedAsync();

        var response = await PatchAsync(s.Client, s.Id, new { }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "body");
    }

    [Fact]
    public async Task Patch_NullTitle_Returns422()
    {
        var s = await SeedAsync();

        var response = await PatchAsync(s.Client, s.Id, new { title = (string?)null }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "title");
    }

    [Fact]
    public async Task Patch_WritesHistoryAndOutboxInOneSave()
    {
        var s = await SeedAsync();

        (await PatchAsync(
                s.Client, s.Id, new { title = "Checkout fails twice", severity = "Low", reason = "Only some users" }, await ETagAsync(s.Client, s.Number)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await HistoryAsync(factory, s.Id)).Select(h => h.Field).Should().Contain(["Title", "Severity"]);
        (await OutboxTypesAsync(factory, s.Id)).Count(t => t == nameof(IncidentUpdated)).Should().Be(2);
    }

    [Fact]
    public async Task Patch_NoToken_Returns401()
    {
        var response = await PatchAsync(factory.CreateClient(), Guid.NewGuid(), new { title = "No token here" }, "\"AAAA\"");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
