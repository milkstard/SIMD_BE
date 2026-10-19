using System.Net;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Hubs;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Sla.EvaluateSla;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using IncidentHub.Infrastructure.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using StackExchange.Redis;
using static IncidentHub.Api.IntegrationTests.Hubs.BroadcastClient;
using static IncidentHub.Api.IntegrationTests.Incidents.IncidentWriteHelpers;

namespace IncidentHub.Api.IntegrationTests.Sla;

/// <summary>
/// The SLA monitor runs in the Worker, whose post-commit dispatcher publishes on Redis; the API relays it to the hub (spec 12_04).
/// The tests build the Worker's context by hand (<see cref="AppDbContext"/> with a <see cref="RedisChangePublisher"/>), so the API-side
/// hook broadcasts nothing and everything a client sees came through the channel.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class WorkerChangePropagationTests(ApiFactory factory)
{
    private readonly SlaMonitorSupport _sla = new(factory);

    private sealed record Scenario(Guid IncidentId, Guid AppId, string Oid);

    /// <summary>An incident whose resolve window ended a minute ago (or ends in an hour) and a responder of its team.</summary>
    private async Task<Scenario> SeedAsync(bool overdue = true)
    {
        var appId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var now = DateTimeOffset.UtcNow;
        var id = overdue
            ? await _sla.SeedAsync(now.AddHours(-2), now.AddMinutes(-1), applicationId: appId)
            : await _sla.SeedAsync(now.AddMinutes(30), now.AddHours(1), applicationId: appId);
        return new Scenario(id, appId, oid);
    }

    private AppDbContext NewWorkerContext(IConnectionMultiplexer redis, ILogger<RedisChangePublisher> publisherLogger)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Sql"])
            .Options;
        var publisher = new RedisChangePublisher(redis, new ProcessOrigin(), publisherLogger);
        return new AppDbContext(options, new PostCommitHook(publisher, TimeProvider.System, NullLogger<PostCommitHook>.Instance));
    }

    /// <summary>One monitor pass the way the Worker commits it: a context whose post-commit dispatcher is the Redis publisher.</summary>
    private async Task RunWorkerPassAsync(IConnectionMultiplexer? redis = null, ILogger<RedisChangePublisher>? publisherLogger = null)
    {
        await using var db = NewWorkerContext(
            redis ?? factory.Services.GetRequiredService<IConnectionMultiplexer>(),
            publisherLogger ?? NullLogger<RedisChangePublisher>.Instance);
        await SlaMonitorSupport.NewHandler(db).Handle(new EvaluateSlaCommand(), CancellationToken.None);
    }

    private static int CountOf<T>(List<T> received, Func<T, bool> match)
    {
        lock (received)
        {
            return received.Count(match);
        }
    }

    [Fact]
    public async Task Breach_WorkerCommit_ClientReceivesIncidentUpdated()
    {
        var s = await SeedAsync();
        await using var client = await ConnectAsync(factory, s.Oid, UserRole.Responder);

        await RunWorkerPassAsync();

        var received = await WaitForAsync(client.Updated, u => u.Id == s.IncidentId);
        received.IsSlaBreached.Should().BeTrue("the summary is read after the Worker's commit");
    }

    [Fact]
    public async Task Breach_WorkerCommit_RefreshesTheDashboardSummaryWithoutWaitingForTheTtl()
    {
        var s = await SeedAsync();
        var rest = factory.CreateClient().AsUser(factory, s.Oid, UserRole.Responder);
        await using var client = await ConnectAsync(factory, s.Oid, UserRole.Responder);
        (await SlaBreachedCountAsync(rest)).Should().Be(0, "fills the 5 s summary cache before the breach");

        await RunWorkerPassAsync();

        await WaitForAsync(client.Dashboards, d => d.SlaBreached == 1);
        (await SlaBreachedCountAsync(rest)).Should().Be(1);
    }

    [Fact]
    public async Task Breach_StatusUnchanged_NoAppStatusChanged()
    {
        var s = await SeedAsync();
        await using var client = await ConnectAsync(factory, s.Oid, UserRole.Responder);

        await RunWorkerPassAsync();

        await WaitForAsync(client.Updated, u => u.Id == s.IncidentId);
        (await ReceivesNoneAsync(client.AppStatuses, a => a.Application.Id == s.AppId)).Should().BeTrue();
    }

    [Fact]
    public async Task Breach_RedisPublishFails_SaveStillCommits()
    {
        var s = await SeedAsync();
        var logger = new FakeLogger<RedisChangePublisher>();
        await using var dead = await ConnectionMultiplexer.ConnectAsync(
            "localhost:1,abortConnect=false,connectTimeout=200,syncTimeout=300,asyncTimeout=300");

        await RunWorkerPassAsync(dead, logger);

        var state = await _sla.StateAsync(s.IncidentId);
        state.IsSlaBreached.Should().BeTrue();
        state.BreachRows.Should().Be(1);
        logger.Collector.GetSnapshot().Should().Contain(r => r.Level == LogLevel.Warning && r.Message.Contains(s.IncidentId.ToString()));
    }

    [Fact]
    public async Task Breach_TwoApiInstances_EachClientReceivesTheEventOnce()
    {
        var s = await SeedAsync();
        var otherOid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(otherOid, s.AppId);
        using var hostB = factory.WithWebHostBuilder(_ => { });
        // Team group only (no dashboard): an IncidentUpdated reaches such a client once, so a second copy would be a double relay.
        await using var onA = await ConnectAsync(factory, s.Oid, UserRole.Responder, joinDashboard: false);
        await using var onB = await ConnectAsync(factory, otherOid, UserRole.Responder, hostB, joinDashboard: false);

        await RunWorkerPassAsync();

        await WaitForAsync(onA.Updated, u => u.Id == s.IncidentId);
        await WaitForAsync(onB.Updated, u => u.Id == s.IncidentId);
        await Task.Delay(TimeSpan.FromSeconds(1));
        CountOf(onA.Updated, u => u.Id == s.IncidentId).Should().Be(1, "only one instance relays a message; the backplane reaches every client");
        CountOf(onB.Updated, u => u.Id == s.IncidentId).Should().Be(1);
    }

    [Fact]
    public async Task Transition_ApiCommit_IsBroadcastOnceAndNotRelayedAgain()
    {
        var s = await SeedAsync(overdue: false);
        var rest = factory.CreateClient().AsUser(factory, s.Oid, UserRole.Responder);
        await using var client = await ConnectAsync(factory, s.Oid, UserRole.Responder, joinDashboard: false);
        var number = await NumberAsync(s.IncidentId);

        var response = await PostAsync(rest, s.IncidentId, "transitions", new { to = "Triaged" }, await ETagAsync(rest, number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await WaitForAsync(client.Updated, u => u.Id == s.IncidentId);
        await Task.Delay(TimeSpan.FromSeconds(1));
        CountOf(client.Updated, u => u.Id == s.IncidentId).Should().Be(1);
    }

    private static async Task<int> SlaBreachedCountAsync(HttpClient rest)
    {
        var summary = await JsonAsync(await rest.GetAsync("/api/v1/dashboard/summary?range=7d"));
        return summary.GetProperty("slaBreached").GetInt32();
    }

    private async Task<string> NumberAsync(Guid incidentId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var number = await db.Incidents.AsNoTracking().Where(i => i.Id == incidentId).Select(i => i.Number).SingleAsync();
        return $"INC-{number}";
    }
}
