using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static IncidentHub.Api.IntegrationTests.Incidents.IncidentWriteHelpers;

namespace IncidentHub.Api.IntegrationTests.Incidents;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class HistoryTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    private static string NewOid() => Guid.NewGuid().ToString();

    private sealed record Scenario(HttpClient Client, Guid AppId, Guid Id, string Number, Guid CallerId, string Oid);

    private async Task<Scenario> SeedAsync(UserRole role = UserRole.Responder)
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        var callerId = await factory.EnsureUserAsync(oid);
        var (id, number) = await _seeder.AddIncidentAsync(
            appId,
            $"Checkout fails {Guid.NewGuid():N}",
            reporterId: role == UserRole.Reporter ? callerId : null);
        return new Scenario(factory.CreateClient().AsUser(factory, oid, role), appId, id, number, callerId, oid);
    }

    private async Task<HttpClient> TeammateAsync(Scenario s, UserRole role)
    {
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, s.AppId);
        await factory.EnsureUserAsync(oid);
        return factory.CreateClient().AsUser(factory, oid, role);
    }

    private static Task<HttpResponseMessage> HistoryAsync(HttpClient client, Guid id, string query = "") =>
        client.GetAsync($"/api/v1/incidents/{id}/history{query}");

    private static async Task<List<JsonElement>> ItemsAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await JsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
    }

    [Fact]
    public async Task History_NewestFirst_Paged()
    {
        var s = await SeedAsync();
        (await PostAsync(s.Client, s.Id, "transitions", new { to = "Triaged" }, await ETagAsync(s.Client, s.Number)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(s.Client, s.Id, "transitions", new { to = "InProgress" }, await ETagAsync(s.Client, s.Number)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var first = await JsonAsync(await HistoryAsync(s.Client, s.Id, "?limit=2"));
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await JsonAsync(await HistoryAsync(s.Client, s.Id, $"?limit=2&cursor={Uri.EscapeDataString(cursor!)}"));

        var all = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).ToList();
        all.Select(i => i.GetProperty("at").GetDateTimeOffset()).Should().BeInDescendingOrder();
        all.Should().HaveCount(3);
        all[0].GetProperty("newValue").GetString().Should().Be("InProgress");
        all[^1].GetProperty("newValue").GetString().Should().Be("New");
        second.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task History_AfterComment_ContainsCommentRow()
    {
        var s = await SeedAsync();
        var posted = await s.Client.PostAsJsonAsync($"/api/v1/incidents/{s.Id}/comments", new { body = "<p>x</p>" });
        var commentId = (await JsonAsync(posted)).GetProperty("id").GetGuid();

        var items = await ItemsAsync(await HistoryAsync(s.Client, s.Id));

        var row = items.Should().ContainSingle(i => i.GetProperty("field").GetString() == "Comment").Subject;
        row.GetProperty("newValue").GetString().Should().Be(commentId.ToString("D"));
        row.GetProperty("actor").GetProperty("id").GetGuid().Should().Be(s.CallerId);
    }

    [Fact]
    public async Task History_InternalComment_HiddenFromReporterVisibleToResponder()
    {
        var s = await SeedAsync(UserRole.Reporter);
        var responder = await TeammateAsync(s, UserRole.Responder);
        await responder.PostAsJsonAsync($"/api/v1/incidents/{s.Id}/comments", new { body = "<p>secret</p>", isInternal = true });
        await responder.PostAsJsonAsync($"/api/v1/incidents/{s.Id}/comments", new { body = "<p>public</p>" });

        var reporterFields = (await ItemsAsync(await HistoryAsync(s.Client, s.Id, "?includeTotal=true")))
            .Select(i => i.GetProperty("field").GetString()).ToList();
        var responderFields = (await ItemsAsync(await HistoryAsync(responder, s.Id)))
            .Select(i => i.GetProperty("field").GetString()).ToList();

        reporterFields.Should().Contain("Comment").And.NotContain("Internal comment");
        responderFields.Should().Contain(["Comment", "Internal comment"]);
    }

    [Fact]
    public async Task History_ReporterTotalCount_ExcludesInternalRows()
    {
        var s = await SeedAsync(UserRole.Reporter);
        var responder = await TeammateAsync(s, UserRole.Responder);
        await responder.PostAsJsonAsync($"/api/v1/incidents/{s.Id}/comments", new { body = "<p>secret</p>", isInternal = true });

        var reporter = await JsonAsync(await HistoryAsync(s.Client, s.Id, "?includeTotal=true"));
        var lead = await JsonAsync(await HistoryAsync(responder, s.Id, "?includeTotal=true"));

        lead.GetProperty("totalCount").GetInt32().Should().Be(reporter.GetProperty("totalCount").GetInt32() + 1);
    }

    [Fact]
    public async Task History_ResolvesAssigneeAndTeamNames()
    {
        var s = await SeedAsync();
        var teammateOid = NewOid();
        await factory.AddToTeamAsync(teammateOid, s.AppId);
        var teammateId = await factory.EnsureUserAsync(teammateOid);
        var teamId = await factory.CreateTeamAsync();
        (await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = teammateId, teamId }, await ETagAsync(s.Client, s.Number)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ItemsAsync(await HistoryAsync(s.Client, s.Id));

        var assignee = items.Single(i => i.GetProperty("field").GetString() == "Assignee");
        assignee.GetProperty("oldValue").ValueKind.Should().Be(JsonValueKind.Null);
        assignee.GetProperty("newValue").GetString().Should().NotBe(teammateId.ToString("D")).And.NotBeNullOrEmpty();
        var team = items.Single(i => i.GetProperty("field").GetString() == "Team");
        team.GetProperty("newValue").GetString().Should().NotBe(teamId.ToString("D")).And.NotBeNullOrEmpty();
    }

    [Fact]
    public async Task History_DuplicateOf_ShowsIncidentNumber()
    {
        var s = await SeedAsync();
        var (originalId, originalNumber) = await _seeder.AddIncidentAsync(s.AppId, $"Original {Guid.NewGuid():N}");
        var body = new { to = "Closed", closeReason = "Duplicate", duplicateOfId = originalId };
        (await PostAsync(s.Client, s.Id, "transitions", body, await ETagAsync(s.Client, s.Number)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await ItemsAsync(await HistoryAsync(s.Client, s.Id));

        items.Single(i => i.GetProperty("field").GetString() == "Duplicate of")
            .GetProperty("newValue").GetString().Should().Be(originalNumber);
    }

    [Fact]
    public async Task History_SystemEntry_HasNullActor()
    {
        var s = await SeedAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var incident = await db.Incidents.SingleAsync(i => i.Id == s.Id);
            incident.MarkSlaBreached(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var items = await ItemsAsync(await HistoryAsync(s.Client, s.Id));

        items.Single(i => i.GetProperty("field").GetString() == "SLA breached")
            .GetProperty("actor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?cursor=nonsense")]
    public async Task History_BadLimitOrCursor_Returns422(string query)
    {
        var s = await SeedAsync();

        (await HistoryAsync(s.Client, s.Id, query)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task History_NonMemberOrUnknown_Returns404()
    {
        var s = await SeedAsync();
        var outsider = factory.CreateClient().AsUser(factory, NewOid(), UserRole.Admin);

        (await HistoryAsync(outsider, s.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await HistoryAsync(s.Client, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task History_NoToken_Returns401()
    {
        (await HistoryAsync(factory.CreateClient(), Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
