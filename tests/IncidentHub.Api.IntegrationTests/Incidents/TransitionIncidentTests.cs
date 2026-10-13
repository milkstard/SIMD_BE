using System.Net;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static IncidentHub.Api.IntegrationTests.Incidents.IncidentWriteHelpers;

namespace IncidentHub.Api.IntegrationTests.Incidents;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TransitionIncidentTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    private static string NewOid() => Guid.NewGuid().ToString();

    /// <summary>A team member calling as <paramref name="kind"/> against an incident in <paramref name="from"/>.</summary>
    private async Task<Scenario> SeedAsync(IncidentStatus from, string kind = "Responder")
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        var callerId = await factory.EnsureUserAsync(oid);
        var role = kind == "Responder" ? UserRole.Responder : UserRole.Reporter;

        var (id, number) = await _seeder.AddIncidentAsync(
            appId,
            $"Checkout fails {Guid.NewGuid():N}",
            reporterId: kind == "Reporter" ? callerId : null,
            configure: (incident, actor, at) =>
            {
                if (kind == "Assignee")
                {
                    incident.Assign(callerId, null, actor, at);
                }

                MoveTo(incident, actor, at, from);
            });

        return new Scenario(factory.CreateClient().AsUser(factory, oid, role), appId, id, number, callerId);
    }

    private sealed record Scenario(HttpClient Client, Guid AppId, Guid Id, string Number, Guid CallerId);

    [Theory]
    [InlineData(IncidentStatus.New, IncidentStatus.Triaged, "Responder")]
    [InlineData(IncidentStatus.New, IncidentStatus.Closed, "Responder")]
    [InlineData(IncidentStatus.Triaged, IncidentStatus.InProgress, "Responder")]
    [InlineData(IncidentStatus.Triaged, IncidentStatus.OnHold, "Responder")]
    [InlineData(IncidentStatus.InProgress, IncidentStatus.Resolved, "Assignee")]
    [InlineData(IncidentStatus.InProgress, IncidentStatus.OnHold, "Assignee")]
    [InlineData(IncidentStatus.OnHold, IncidentStatus.InProgress, "Assignee")]
    [InlineData(IncidentStatus.Resolved, IncidentStatus.Closed, "Reporter")]
    [InlineData(IncidentStatus.Resolved, IncidentStatus.Reopened, "Reporter")]
    [InlineData(IncidentStatus.Reopened, IncidentStatus.InProgress, "Responder")]
    public async Task Transition_AllowedPerRole_Returns200(IncidentStatus from, IncidentStatus to, string kind)
    {
        var s = await SeedAsync(from, kind);
        var etag = await ETagAsync(s.Client, s.Number);
        var closeReason = to != IncidentStatus.Closed ? null : from == IncidentStatus.New ? "Invalid" : "Fixed";

        var response = await PostAsync(s.Client, s.Id, "transitions", new { to = to.ToString(), note = "Because", closeReason }, etag);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("status").GetString().Should().Be(to.ToString());
        body.GetProperty("rowVersion").GetString().Should().NotBe(etag.Trim('"'));
        response.Headers.ETag!.Tag.Should().Be($"\"{body.GetProperty("rowVersion").GetString()}\"");
        body.GetProperty("allowedTransitions").GetArrayLength().Should().BeGreaterThanOrEqualTo(0);

        var history = (await HistoryAsync(factory, s.Id))
            .Where(h => h.Field == "Status" && h.OldValue == from.ToString() && h.NewValue == to.ToString() && h.Note == "Because")
            .ToList();
        var row = history.Should().ContainSingle().Subject;
        row.ActorId.Should().Be(s.CallerId);
    }

    [Fact]
    public async Task Transition_ToTriaged_ReturnsUpdatedAllowedTransitionsAndAcknowledgedAt()
    {
        var s = await SeedAsync(IncidentStatus.New);

        var response = await PostAsync(s.Client, s.Id, "transitions", new { to = "Triaged" }, await ETagAsync(s.Client, s.Number));

        var body = await JsonAsync(response);
        body.GetProperty("allowedTransitions").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["InProgress", "OnHold"]);
        body.GetProperty("acknowledgedAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Transition_ResolvedMoves_SetsResolvedAndClosedFields()
    {
        var s = await SeedAsync(IncidentStatus.InProgress);
        var resolved = await JsonAsync(await PostAsync(s.Client, s.Id, "transitions", new { to = "Resolved", note = "Fixed it" }, await ETagAsync(s.Client, s.Number)));
        resolved.GetProperty("resolvedAt").ValueKind.Should().Be(JsonValueKind.String);

        var closed = await JsonAsync(await PostAsync(
            s.Client, s.Id, "transitions", new { to = "Closed", closeReason = "Fixed" }, await ETagAsync(s.Client, s.Number)));

        closed.GetProperty("closedAt").ValueKind.Should().Be(JsonValueKind.String);
        closed.GetProperty("closeReason").GetString().Should().Be("Fixed");
        closed.GetProperty("allowedTransitions").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Transition_NotInTable_Returns409()
    {
        var s = await SeedAsync(IncidentStatus.New);

        var response = await PostAsync(s.Client, s.Id, "transitions", new { to = "Resolved", note = "Nope" }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(response)).GetProperty("type").GetString().Should().EndWith("invalid-transition");
    }

    [Fact]
    public async Task Transition_NotPermitted_Returns403()
    {
        var s = await SeedAsync(IncidentStatus.New, "Reporter");

        var response = await PostAsync(s.Client, s.Id, "transitions", new { to = "Triaged" }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(response)).GetProperty("title").GetString().Should().Be("Transition not permitted");
    }

    [Fact]
    public async Task Transition_ResolvedWithoutNote_Returns422()
    {
        var s = await SeedAsync(IncidentStatus.InProgress);

        var response = await PostAsync(s.Client, s.Id, "transitions", new { to = "Resolved" }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "note");
    }

    [Fact]
    public async Task Transition_CloseWithoutReason_Returns422()
    {
        var s = await SeedAsync(IncidentStatus.New);

        var response = await PostAsync(s.Client, s.Id, "transitions", new { to = "Closed" }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "closeReason");
    }

    [Fact]
    public async Task Transition_DuplicateWithoutTarget_Returns422()
    {
        var s = await SeedAsync(IncidentStatus.New);

        var response = await PostAsync(
            s.Client, s.Id, "transitions", new { to = "Closed", closeReason = "Duplicate" }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "duplicateOfId");
    }

    [Fact]
    public async Task Transition_MissingTo_Returns422()
    {
        var s = await SeedAsync(IncidentStatus.New);

        var response = await PostAsync(s.Client, s.Id, "transitions", new { note = "x" }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "to");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("self")]
    public async Task Transition_DuplicateOfUnknownOrSelf_Returns422(string target)
    {
        var s = await SeedAsync(IncidentStatus.New);
        var duplicateOfId = target == "self" ? s.Id : Guid.NewGuid();

        var response = await PostAsync(
            s.Client, s.Id, "transitions", new { to = "Closed", closeReason = "Duplicate", duplicateOfId }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "duplicateOfId");
        (await HistoryAsync(factory, s.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Transition_NotPermittedWithBadDuplicateOf_Returns403Not422()
    {
        var s = await SeedAsync(IncidentStatus.New, "Reporter");

        var response = await PostAsync(
            s.Client, s.Id, "transitions", new { to = "Closed", closeReason = "Duplicate", duplicateOfId = Guid.NewGuid() }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Transition_NotInTableWithBadDuplicateOf_Returns409Not422()
    {
        var s = await SeedAsync(IncidentStatus.New);

        var response = await PostAsync(
            s.Client, s.Id, "transitions", new { to = "Resolved", note = "x", closeReason = "Duplicate", duplicateOfId = Guid.NewGuid() }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Transition_DuplicateOfInvisibleIncident_Returns422()
    {
        var s = await SeedAsync(IncidentStatus.New);
        var foreignApp = await factory.CreateAppAsync();
        var (foreignId, _) = await _seeder.AddIncidentAsync(foreignApp, $"Foreign incident {Guid.NewGuid():N}");

        var response = await PostAsync(
            s.Client, s.Id, "transitions", new { to = "Closed", closeReason = "Duplicate", duplicateOfId = foreignId }, await ETagAsync(s.Client, s.Number));

        await AssertErrorKeyAsync(response, "duplicateOfId");
    }

    [Fact]
    public async Task Transition_DuplicateOfVisibleIncident_Returns200WithDuplicateOf()
    {
        var s = await SeedAsync(IncidentStatus.New);
        var (originalId, originalNumber) = await _seeder.AddIncidentAsync(s.AppId, $"The original {Guid.NewGuid():N}");

        var response = await PostAsync(
            s.Client, s.Id, "transitions", new { to = "Closed", closeReason = "Duplicate", duplicateOfId = originalId }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("closeReason").GetString().Should().Be("Duplicate");
        body.GetProperty("duplicateOf").GetProperty("id").GetGuid().Should().Be(originalId);
        body.GetProperty("duplicateOf").GetProperty("number").GetString().Should().Be(originalNumber);
    }

    [Fact]
    public async Task Transition_StaleIfMatch_Returns409AndChangesNothing()
    {
        var s = await SeedAsync(IncidentStatus.New);
        var stale = await ETagAsync(s.Client, s.Number);
        (await PostAsync(s.Client, s.Id, "transitions", new { to = "Triaged" }, stale)).StatusCode.Should().Be(HttpStatusCode.OK);
        var historyBefore = (await HistoryAsync(factory, s.Id)).Count;
        var outboxBefore = (await OutboxTypesAsync(factory, s.Id)).Count;

        var response = await PostAsync(s.Client, s.Id, "transitions", new { to = "InProgress" }, stale);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await HistoryAsync(factory, s.Id)).Should().HaveCount(historyBefore);
        (await OutboxTypesAsync(factory, s.Id)).Should().HaveCount(outboxBefore);
        (await JsonAsync(await s.Client.GetAsync(Route(s.Number)))).GetProperty("status").GetString().Should().Be("Triaged");
    }

    [Fact]
    public async Task Transition_NoIfMatch_Returns428()
    {
        var s = await SeedAsync(IncidentStatus.New);

        var response = await PostAsync(s.Client, s.Id, "transitions", new { to = "Triaged" }, null);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task Transition_MalformedIfMatch_Returns400()
    {
        var s = await SeedAsync(IncidentStatus.New);

        var response = await PostAsync(s.Client, s.Id, "transitions", new { to = "Triaged" }, "\"not base64!\"");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Transition_ConcurrentSameEtag_OneWins()
    {
        var s = await SeedAsync(IncidentStatus.New);
        var etag = await ETagAsync(s.Client, s.Number);

        var responses = await Task.WhenAll(
            PostAsync(s.Client, s.Id, "transitions", new { to = "Triaged" }, etag),
            PostAsync(s.Client, s.Id, "transitions", new { to = "Triaged" }, etag));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await HistoryAsync(factory, s.Id)).Count(h => h.Field == "Status" && h.NewValue == "Triaged").Should().Be(1);
    }

    [Fact]
    public async Task Transition_WritesHistoryAndOutboxInOneSave()
    {
        var s = await SeedAsync(IncidentStatus.New);

        (await PostAsync(s.Client, s.Id, "transitions", new { to = "Triaged" }, await ETagAsync(s.Client, s.Number)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await HistoryAsync(factory, s.Id)).Select(h => h.Field).Should().Contain("Status");
        (await OutboxTypesAsync(factory, s.Id)).Should().Contain(nameof(IncidentTransitioned));
    }

    [Fact]
    public async Task Transition_NonMember_Returns404AndChangesNothing()
    {
        var s = await SeedAsync(IncidentStatus.New);
        var etag = await ETagAsync(s.Client, s.Number);
        var admin = factory.CreateClient().AsUser(factory, NewOid(), UserRole.Admin);

        var response = await PostAsync(admin, s.Id, "transitions", new { to = "Triaged" }, etag);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await JsonAsync(await s.Client.GetAsync(Route(s.Number)))).GetProperty("status").GetString().Should().Be("New");
    }

    [Fact]
    public async Task Transition_UnknownId_Returns404()
    {
        var s = await SeedAsync(IncidentStatus.New);

        var response = await PostAsync(s.Client, Guid.NewGuid(), "transitions", new { to = "Triaged" }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Transition_NoToken_Returns401()
    {
        var response = await PostAsync(factory.CreateClient(), Guid.NewGuid(), "transitions", new { to = "Triaged" }, "\"AAAA\"");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Transition_ConcurrentSave_RollsBackHistoryAndOutbox()
    {
        var s = await SeedAsync(IncidentStatus.New);
        var responder = new Actor(s.CallerId, new HashSet<UserRole> { UserRole.Responder });

        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var winner = await first.Incidents.SingleAsync(i => i.Id == s.Id);
        var loser = await second.Incidents.SingleAsync(i => i.Id == s.Id);
        var historyBefore = (await HistoryAsync(factory, s.Id)).Count;
        var outboxBefore = (await OutboxTypesAsync(factory, s.Id)).Count;

        winner.TransitionTo(IncidentStatus.Triaged, responder, null, DateTimeOffset.UtcNow);
        await first.SaveChangesAsync();
        loser.TransitionTo(IncidentStatus.Closed, responder, null, DateTimeOffset.UtcNow, CloseReason.Invalid);
        var save = () => second.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();

        (await HistoryAsync(factory, s.Id)).Should().HaveCount(historyBefore + 1);
        (await OutboxTypesAsync(factory, s.Id)).Should().HaveCount(outboxBefore + 1);
    }

    private static async Task AssertErrorKeyAsync(HttpResponseMessage response, string key)
    {
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("errors").TryGetProperty(key, out _).Should().BeTrue($"errors should name {key}");
    }
}
