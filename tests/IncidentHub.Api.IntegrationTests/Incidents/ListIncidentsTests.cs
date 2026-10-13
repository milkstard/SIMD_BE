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

namespace IncidentHub.Api.IntegrationTests.Incidents;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ListIncidentsTests(ApiFactory factory)
{
    private const string Route = "/api/v1/incidents";

    private readonly IncidentSeeder _seeder = new(factory);

    private static string NewOid() => Guid.NewGuid().ToString();

    private async Task<(HttpClient Client, Guid AppId, Guid UserId)> MemberAsync(UserRole role = UserRole.Responder)
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        var userId = await factory.EnsureUserAsync(oid);
        return (factory.CreateClient().AsUser(factory, oid, role), appId, userId);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> GetPageAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"{Route}?{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await JsonAsync(response);
    }

    private static Guid[] Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray();

    private static async Task<Guid[]> IdsAsync(HttpClient client, string query) => Ids(await GetPageAsync(client, query));

    private async Task<Guid> OwningTeamAsync(Guid appId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Applications.Where(a => a.Id == appId).Select(a => a.OwningTeamId).SingleAsync();
    }

    [Fact]
    public async Task List_Default_ReturnsNewestFirstVisibleOnly()
    {
        var (client, appId, _) = await MemberAsync();
        var otherApp = await factory.CreateAppAsync();
        var now = DateTimeOffset.UtcNow;
        var oldest = (await _seeder.AddIncidentAsync(appId, "Oldest incident", at: now.AddMinutes(-30))).Id;
        var newest = (await _seeder.AddIncidentAsync(appId, "Newest incident", at: now.AddMinutes(-10))).Id;
        var middle = (await _seeder.AddIncidentAsync(appId, "Middle incident", at: now.AddMinutes(-20))).Id;
        var foreign = (await _seeder.AddIncidentAsync(otherApp, "Foreign incident", at: now)).Id;

        var page = await GetPageAsync(client, string.Empty);

        Ids(page).Should().Equal(newest, middle, oldest);
        Ids(page).Should().NotContain(foreign);
        page.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
        page.GetProperty("totalCount").ValueKind.Should().Be(JsonValueKind.Null);
        var item = page.GetProperty("items")[0];
        item.GetProperty("number").GetString().Should().StartWith("INC-");
        item.GetProperty("application").GetProperty("id").GetGuid().Should().Be(appId);
        item.GetProperty("rowVersion").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("-createdAt")]
    [InlineData("createdAt")]
    [InlineData("severity")]
    [InlineData("-updatedAt")]
    public async Task List_PagesWithCursor_ReturnsEveryIncidentOnceInSortOrder(string sort)
    {
        var (client, appId, _) = await MemberAsync();
        var now = DateTimeOffset.UtcNow.AddHours(-2);
        Severity[] severities = [Severity.Low, Severity.Critical, Severity.Medium, Severity.High, Severity.Critical, Severity.Low, Severity.High];
        for (var i = 0; i < severities.Length; i++)
        {
            await _seeder.AddIncidentAsync(appId, $"Incident number {i}", severity: severities[i], at: now.AddMinutes(i));
        }

        var everything = Ids(await GetPageAsync(client, $"sort={sort}&limit=100"));
        var paged = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await GetPageAsync(client, $"sort={sort}&limit=3" + (cursor is null ? string.Empty : $"&cursor={cursor}"));
            paged.AddRange(Ids(page));
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        everything.Should().HaveCount(severities.Length);
        paged.Should().Equal(everything);
        paged.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task List_SortSeverity_MostSevereFirstThenNewest()
    {
        var (client, appId, _) = await MemberAsync();
        var now = DateTimeOffset.UtcNow.AddHours(-1);
        var lowOld = (await _seeder.AddIncidentAsync(appId, "Low old", severity: Severity.Low, at: now)).Id;
        var critical = (await _seeder.AddIncidentAsync(appId, "Critical one", severity: Severity.Critical, at: now.AddMinutes(1))).Id;
        var mediumOld = (await _seeder.AddIncidentAsync(appId, "Medium old", severity: Severity.Medium, at: now.AddMinutes(2))).Id;
        var mediumNew = (await _seeder.AddIncidentAsync(appId, "Medium new", severity: Severity.Medium, at: now.AddMinutes(3))).Id;
        var high = (await _seeder.AddIncidentAsync(appId, "High one", severity: Severity.High, at: now.AddMinutes(4))).Id;

        (await IdsAsync(client, "sort=severity")).Should().Equal(critical, high, mediumNew, mediumOld, lowOld);
    }

    [Fact]
    public async Task List_FilterByEachField_ReturnsMatches()
    {
        var (client, appId, me) = await MemberAsync();
        var teamId = await OwningTeamAsync(appId);
        var other = await factory.EnsureUserAsync(NewOid());
        var now = DateTimeOffset.UtcNow.AddHours(-1);

        var plain = (await _seeder.AddIncidentAsync(appId, "Plain high", severity: Severity.High, at: now)).Id;
        var critical = (await _seeder.AddIncidentAsync(appId, "Critical thing", severity: Severity.Critical, at: now.AddMinutes(1))).Id;
        var mine = (await _seeder.AddIncidentAsync(
            appId, "Assigned to me", reporterId: me, severity: Severity.Medium, at: now.AddMinutes(2),
            configure: (inc, actor, at) => inc.Assign(me, teamId, actor, at))).Id;
        var theirs = (await _seeder.AddIncidentAsync(
            appId, "Assigned to other", severity: Severity.Low, at: now.AddMinutes(3),
            configure: (inc, actor, at) => inc.Assign(other, null, actor, at))).Id;
        var triaged = (await _seeder.AddIncidentAsync(
            appId, "Triaged one", severity: Severity.Medium, at: now.AddMinutes(4),
            configure: (inc, actor, at) => inc.TransitionTo(IncidentStatus.Triaged, actor, null, at))).Id;
        var breached = (await _seeder.AddIncidentAsync(
            appId, "Breached one", severity: Severity.Medium, at: now.AddMinutes(5),
            configure: (inc, _, at) => inc.MarkSlaBreached(at))).Id;

        (await IdsAsync(client, "severity=Critical")).Should().Equal(critical);
        (await IdsAsync(client, "severity=Critical&severity=Low")).Should().BeEquivalentTo([critical, theirs]);
        (await IdsAsync(client, "status=Triaged")).Should().Equal(triaged);
        (await IdsAsync(client, "status=Triaged&status=Closed")).Should().Equal(triaged);
        (await IdsAsync(client, $"applicationId={appId}")).Should().HaveCount(6);
        (await IdsAsync(client, "environment=Production")).Should().HaveCount(6);
        (await IdsAsync(client, "environment=UAT")).Should().BeEmpty();
        (await IdsAsync(client, "assigneeId=me")).Should().Equal(mine);
        (await IdsAsync(client, $"assigneeId={other}")).Should().Equal(theirs);
        (await IdsAsync(client, $"teamId={teamId}")).Should().Equal(mine);
        (await IdsAsync(client, "reporterId=me")).Should().Equal(mine);
        (await IdsAsync(client, "slaBreached=true")).Should().Equal(breached);
        (await IdsAsync(client, "slaBreached=false")).Should().HaveCount(5).And.NotContain(breached);
        (await IdsAsync(client, $"createdFrom={Uri.EscapeDataString(now.AddMinutes(4).ToString("o"))}")).Should().BeEquivalentTo([triaged, breached]);
        (await IdsAsync(client, $"createdTo={Uri.EscapeDataString(now.AddMinutes(1).ToString("o"))}")).Should().BeEquivalentTo([plain, critical]);
        (await IdsAsync(client, "status=New&severity=Medium&assigneeId=unassigned")).Should().BeEquivalentTo([breached]);
    }

    [Fact]
    public async Task List_Unassigned_ReturnsOnlyUnassigned()
    {
        var (client, appId, me) = await MemberAsync();
        var unassigned = (await _seeder.AddIncidentAsync(appId, "Nobody has this")).Id;
        await _seeder.AddIncidentAsync(
            appId, "Somebody has this", configure: (inc, actor, at) => inc.Assign(me, null, actor, at));

        (await IdsAsync(client, "assigneeId=unassigned")).Should().Equal(unassigned);
    }

    [Fact]
    public async Task List_Q_FindsTitleAndDescription()
    {
        var (client, appId, _) = await MemberAsync();
        var token = $"tok{Guid.NewGuid():N}";
        var inTitle = (await _seeder.AddIncidentAsync(appId, $"Gateway {token} down")).Id;
        var inDescription = (await _seeder.AddIncidentAsync(appId, "Something else entirely", description: $"<p>Trace {token} attached</p>")).Id;
        await _seeder.AddIncidentAsync(appId, "Unrelated incident");

        await IncidentSeeder.EventuallyAsync(async () => (await IdsAsync(client, $"q={token}")).Length == 2);

        (await IdsAsync(client, $"q={token}")).Should().BeEquivalentTo([inTitle, inDescription]);
    }

    [Fact]
    public async Task List_QWithOtherFilter_IntersectsBoth()
    {
        var (client, appId, _) = await MemberAsync();
        var token = $"tok{Guid.NewGuid():N}";
        var critical = (await _seeder.AddIncidentAsync(appId, $"First {token}", severity: Severity.Critical)).Id;
        await _seeder.AddIncidentAsync(appId, $"Second {token}", severity: Severity.Low);

        await IncidentSeeder.EventuallyAsync(async () => (await IdsAsync(client, $"q={token}")).Length == 2);

        (await IdsAsync(client, $"q={token}&severity=Critical")).Should().Equal(critical);
    }

    [Fact]
    public async Task List_QOnlyMatchesInVisibleApplications()
    {
        var (client, _, _) = await MemberAsync();
        var foreignApp = await factory.CreateAppAsync();
        var token = $"tok{Guid.NewGuid():N}";
        await _seeder.AddIncidentAsync(foreignApp, $"Foreign {token}");
        await _seeder.WaitForFullTextPopulationAsync(TimeSpan.FromSeconds(45));

        (await IdsAsync(client, $"q={token}")).Should().BeEmpty();
    }

    [Fact]
    public async Task List_NonMemberApplication_ReturnsEmpty()
    {
        var (client, _, _) = await MemberAsync();
        var foreignApp = await factory.CreateAppAsync();
        await _seeder.AddIncidentAsync(foreignApp, "Hidden incident");

        var page = await GetPageAsync(client, $"applicationId={foreignApp}&includeTotal=true");

        Ids(page).Should().BeEmpty();
        page.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task List_AdminNotMember_SeesNothingOfThatApplication()
    {
        var foreignApp = await factory.CreateAppAsync();
        await _seeder.AddIncidentAsync(foreignApp, "Hidden from admin");
        var client = factory.CreateClient().AsUser(factory, NewOid(), UserRole.Admin);

        (await IdsAsync(client, string.Empty)).Should().BeEmpty();
        (await IdsAsync(client, $"applicationId={foreignApp}")).Should().BeEmpty();
    }

    [Fact]
    public async Task List_ReporterOnlyUser_SeesTeamIncidents()
    {
        var (client, appId, _) = await MemberAsync(UserRole.Reporter);
        var teamIncident = (await _seeder.AddIncidentAsync(appId, "Reported by a colleague")).Id;

        (await IdsAsync(client, string.Empty)).Should().Equal(teamIncident);
    }

    [Fact]
    public async Task List_IncludeTotal_ReturnsCountOfAllPages()
    {
        var (client, appId, _) = await MemberAsync();
        for (var i = 0; i < 3; i++)
        {
            await _seeder.AddIncidentAsync(appId, $"Counted incident {i}");
        }

        var page = await GetPageAsync(client, "limit=2&includeTotal=true");

        Ids(page).Should().HaveCount(2);
        page.GetProperty("nextCursor").GetString().Should().NotBeNull();
        page.GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("cursor=garbage")]
    [InlineData("assigneeId=nobody")]
    [InlineData("reporterId=unassigned")]
    [InlineData("createdFrom=2026-10-10T00:00:00Z&createdTo=2026-10-01T00:00:00Z")]
    public async Task List_InvalidInput_Returns422(string query)
    {
        var (client, _, _) = await MemberAsync();

        var response = await client.GetAsync($"{Route}?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task List_TamperedCursorFromAnotherSort_Returns422()
    {
        var (client, appId, _) = await MemberAsync();
        for (var i = 0; i < 3; i++)
        {
            await _seeder.AddIncidentAsync(appId, $"Cursor incident {i}");
        }

        var cursor = (await GetPageAsync(client, "limit=1")).GetProperty("nextCursor").GetString();

        var response = await client.GetAsync($"{Route}?sort=severity&cursor={cursor}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData("sort=bogus")]
    [InlineData("status=Nope")]
    [InlineData("applicationId=not-a-guid")]
    public async Task List_MalformedBinding_Returns400(string query)
    {
        var (client, _, _) = await MemberAsync();

        var response = await client.GetAsync($"{Route}?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_NoToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_NoRole_Returns403()
    {
        var client = factory.CreateClient().AsUser(factory, NewOid());

        var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
