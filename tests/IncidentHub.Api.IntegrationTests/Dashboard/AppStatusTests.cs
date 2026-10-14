using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Caching;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using static IncidentHub.Api.IntegrationTests.Incidents.IncidentWriteHelpers;

namespace IncidentHub.Api.IntegrationTests.Dashboard;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AppStatusTests(ApiFactory factory)
{
    private const string Url = "/api/v1/dashboard/app-status";

    private readonly IncidentSeeder _seeder = new(factory);

    private sealed record Member(HttpClient Client, string Oid, Guid AppId);

    private async Task<Member> MemberAsync()
    {
        var app = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, app);
        return new Member(factory.CreateClient().AsUser(factory, oid, UserRole.Responder), oid, app);
    }

    private static async Task<JsonElement?> ItemAsync(HttpClient client, Guid appId)
    {
        var response = await client.GetAsync(Url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var item in (await JsonAsync(response)).EnumerateArray())
        {
            if (item.GetProperty("application").GetProperty("id").GetGuid() == appId)
            {
                return item;
            }
        }

        return null;
    }

    private static object Report(Guid appId, string severity, string title) =>
        new { applicationId = appId, environment = "Production", severity, title, description = "<p>Users see an error page.</p>" };

    [Fact]
    public async Task Get_CriticalProductionReported_ReturnsOutage()
    {
        var m = await MemberAsync();
        (await ItemAsync(m.Client, m.AppId))!.Value.GetProperty("status").GetString().Should().Be("Operational");

        var response = await m.Client.PostAsJsonAsync("/api/v1/incidents", Report(m.AppId, "Critical", $"Checkout down {Guid.NewGuid():N}"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var item = (await ItemAsync(m.Client, m.AppId))!.Value;
        item.GetProperty("status").GetString().Should().Be("Outage");
        item.GetProperty("openIncidents").GetInt32().Should().Be(1);
        item.GetProperty("worstOpenSeverity").GetString().Should().Be("Critical");
        item.GetProperty("since").GetString().Should().NotBeNull();
    }

    [Fact]
    public async Task Get_AfterResolve_ReturnsOperational()
    {
        var m = await MemberAsync();
        var (id, number) = await _seeder.AddIncidentAsync(
            m.AppId,
            $"Checkout down {Guid.NewGuid():N}",
            severity: Severity.Critical,
            configure: (incident, actor, at) =>
            {
                incident.TransitionTo(IncidentStatus.Triaged, actor, null, at);
                incident.TransitionTo(IncidentStatus.InProgress, actor, null, at);
            });
        (await ItemAsync(m.Client, m.AppId))!.Value.GetProperty("status").GetString().Should().Be("Outage");

        var response = await PostAsync(m.Client, id, "transitions", new { to = "Resolved", note = "Gateway timeout raised" }, await ETagAsync(m.Client, number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = (await ItemAsync(m.Client, m.AppId))!.Value;
        item.GetProperty("status").GetString().Should().Be("Operational");
        item.GetProperty("openIncidents").GetInt32().Should().Be(0);
        item.GetProperty("since").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_SeverityEdited_StatusFollows()
    {
        var m = await MemberAsync();
        var (id, number) = await _seeder.AddIncidentAsync(m.AppId, $"Checkout slow {Guid.NewGuid():N}", severity: Severity.Medium);
        (await ItemAsync(m.Client, m.AppId))!.Value.GetProperty("status").GetString().Should().Be("Operational");

        var up = await PatchAsync(m.Client, id, new { severity = "Critical", reason = "Revenue impact" }, await ETagAsync(m.Client, number));
        up.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ItemAsync(m.Client, m.AppId))!.Value.GetProperty("status").GetString().Should().Be("Outage");

        var down = await PatchAsync(m.Client, id, new { severity = "Low", reason = "Workaround found" }, await ETagAsync(m.Client, number));
        down.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ItemAsync(m.Client, m.AppId))!.Value.GetProperty("status").GetString().Should().Be("Operational");
    }

    [Fact]
    public async Task Get_IncidentClosed_ReturnsOperational()
    {
        var m = await MemberAsync();
        await _seeder.AddIncidentAsync(
            m.AppId,
            $"Checkout down {Guid.NewGuid():N}",
            severity: Severity.Critical,
            configure: (incident, actor, at) =>
            {
                incident.TransitionTo(IncidentStatus.Triaged, actor, null, at);
                incident.TransitionTo(IncidentStatus.InProgress, actor, null, at);
                incident.TransitionTo(IncidentStatus.Resolved, actor, "Fixed", at);
                incident.TransitionTo(IncidentStatus.Closed, actor, null, at, CloseReason.Fixed);
            });

        (await ItemAsync(m.Client, m.AppId))!.Value.GetProperty("status").GetString().Should().Be("Operational");
    }

    [Fact]
    public async Task Get_IncidentReopened_StillCountsAsOpen()
    {
        var m = await MemberAsync();
        var (id, number) = await _seeder.AddIncidentAsync(
            m.AppId,
            $"Checkout down {Guid.NewGuid():N}",
            severity: Severity.Critical,
            configure: (incident, actor, at) =>
            {
                incident.TransitionTo(IncidentStatus.Triaged, actor, null, at);
                incident.TransitionTo(IncidentStatus.InProgress, actor, null, at);
                incident.TransitionTo(IncidentStatus.Resolved, actor, "Fixed", at);
            });
        (await ItemAsync(m.Client, m.AppId))!.Value.GetProperty("status").GetString().Should().Be("Operational");

        var response = await PostAsync(m.Client, id, "transitions", new { to = "Reopened", note = "Back again" }, await ETagAsync(m.Client, number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ItemAsync(m.Client, m.AppId))!.Value.GetProperty("status").GetString().Should().Be("Outage");
    }

    [Fact]
    public async Task Get_IncidentMovedToAnotherApplication_BothStatusesFollow()
    {
        var m = await MemberAsync();
        var target = await factory.CreateAppAsync();
        await factory.AddToTeamAsync(m.Oid, target);
        var (id, number) = await _seeder.AddIncidentAsync(m.AppId, $"Checkout down {Guid.NewGuid():N}", severity: Severity.Critical);
        (await ItemAsync(m.Client, m.AppId))!.Value.GetProperty("status").GetString().Should().Be("Outage");

        var response = await PatchAsync(m.Client, id, new { applicationId = target }, await ETagAsync(m.Client, number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ItemAsync(m.Client, m.AppId))!.Value.GetProperty("status").GetString().Should().Be("Operational");
        (await ItemAsync(m.Client, target))!.Value.GetProperty("status").GetString().Should().Be("Outage");
    }

    [Fact]
    public async Task Get_CacheKeyDeleted_FallsBackToSqlAndRepopulates()
    {
        var m = await MemberAsync();
        await _seeder.AddIncidentAsync(m.AppId, $"Checkout down {Guid.NewGuid():N}", severity: Severity.Critical);
        var db = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var key = (RedisKey)$"appstatus:{m.AppId:N}";
        await db.KeyDeleteAsync(key);

        var item = (await ItemAsync(m.Client, m.AppId))!.Value;

        item.GetProperty("status").GetString().Should().Be("Outage");
        (await db.KeyExistsAsync(key)).Should().BeTrue();
    }

    [Fact]
    public async Task Get_OtherTeamApp_Absent()
    {
        var mine = await MemberAsync();
        var other = await MemberAsync();
        await _seeder.AddIncidentAsync(other.AppId, $"Checkout down {Guid.NewGuid():N}", severity: Severity.Critical);

        (await ItemAsync(mine.Client, mine.AppId)).Should().NotBeNull();
        (await ItemAsync(mine.Client, other.AppId)).Should().BeNull();
    }

    [Fact]
    public async Task Get_InactiveApp_IsNotReturned()
    {
        var m = await MemberAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var app = await db.Applications.FindAsync(m.AppId);
            app!.Update(app.Name, app.Code, app.OwningTeamId, app.EscalationUserId, app.Environments, isActive: false);
            await db.SaveChangesAsync();
        }

        (await ItemAsync(m.Client, m.AppId)).Should().BeNull();
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_RedisUnavailable_StillAnswers()
    {
        // The reader is exercised directly: sign-in sync (UserDirectory) also needs Redis, so no authenticated request survives a dead Redis.
        var m = await MemberAsync();
        await _seeder.AddIncidentAsync(m.AppId, $"Checkout down {Guid.NewGuid():N}", severity: Severity.Critical);
        using var dead = ConnectionMultiplexer.Connect("127.0.0.1:1,abortConnect=false,connectTimeout=300,syncTimeout=300,asyncTimeout=300");
        using var scope = factory.Services.CreateScope();
        var reader = new RedisAppStatusReader(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(), dead, NullLogger<RedisAppStatusReader>.Instance);

        var items = await reader.GetAsync([m.AppId], CancellationToken.None);
        var refresh = await reader.RefreshAsync(m.AppId, CancellationToken.None);

        items.Should().ContainSingle().Which.Status.Should().Be(AppStatus.Outage);
        refresh!.Previous.Should().BeNull();
        refresh.Current.Status.Should().Be(AppStatus.Outage);
    }
}
