using System.Net;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Dashboard;
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
public sealed class DashboardSummaryTests(ApiFactory factory)
{
    private const string Url = "/api/v1/dashboard/summary";

    private readonly IncidentSeeder _seeder = new(factory);

    private sealed record Member(HttpClient Client, string Oid, Guid AppId);

    private async Task<Member> MemberAsync()
    {
        var app = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, app);
        return new Member(factory.CreateClient().AsUser(factory, oid, UserRole.Responder), oid, app);
    }

    private static async Task<JsonElement> SummaryAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync(Url + query);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await JsonAsync(response);
    }

    /// <summary>
    /// Seeding saves broadcast through the post-commit hook, which rewrites the team's cache entry from whatever was committed at that
    /// moment. After seeding, drop the entry and read until the summary shows the seeded state.
    /// </summary>
    private async Task<JsonElement> SettledAsync(Member m, Func<JsonElement, bool> seeded, string query = "")
    {
        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (true)
        {
            await redis.KeyDeleteAsync(await CacheKeyAsync(m));
            var summary = await SummaryAsync(m.Client, query);
            if (seeded(summary) || DateTime.UtcNow > deadline)
            {
                return summary;
            }

            await Task.Delay(200);
        }
    }

    private static string Title() => $"Checkout down {Guid.NewGuid():N}";

    [Fact]
    public async Task Get_SeededIncidents_CountsMatch()
    {
        var m = await MemberAsync();
        var assignee = await factory.EnsureUserAsync(Guid.NewGuid().ToString());
        await _seeder.AddIncidentAsync(m.AppId, Title(), severity: Severity.Critical);
        await _seeder.AddIncidentAsync(m.AppId, Title(), severity: Severity.High, configure: (i, actor, at) => i.Assign(assignee, null, actor, at));
        await _seeder.AddIncidentAsync(
            m.AppId, Title(), severity: Severity.High, configure: (i, actor, at) => { i.Assign(assignee, null, actor, at); i.MarkSlaBreached(at); });
        await _seeder.AddIncidentAsync(
            m.AppId,
            Title(),
            severity: Severity.Low,
            configure: (i, actor, at) =>
            {
                i.TransitionTo(IncidentStatus.Triaged, actor, null, at);
                i.TransitionTo(IncidentStatus.InProgress, actor, null, at);
                i.TransitionTo(IncidentStatus.Resolved, actor, "Fixed", at);
            });

        var summary = await SettledAsync(m, x => x.GetProperty("trend")[6].GetProperty("reported").GetInt32() == 4);

        summary.GetProperty("range").GetString().Should().Be("7d");
        summary.GetProperty("openTotal").GetInt32().Should().Be(3);
        summary.GetProperty("unassigned").GetInt32().Should().Be(1);
        summary.GetProperty("slaBreached").GetInt32().Should().Be(1);
        var severities = summary.GetProperty("openBySeverity");
        severities.GetProperty("Critical").GetInt32().Should().Be(1);
        severities.GetProperty("High").GetInt32().Should().Be(2);
        severities.GetProperty("Medium").GetInt32().Should().Be(0);
        severities.GetProperty("Low").GetInt32().Should().Be(0);
        var apps = summary.GetProperty("openByApplication").EnumerateArray().ToList();
        apps.Should().ContainSingle();
        apps[0].GetProperty("application").GetProperty("id").GetGuid().Should().Be(m.AppId);
        apps[0].GetProperty("open").GetInt32().Should().Be(3);
        apps[0].GetProperty("critical").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Get_OtherTeamIncidents_NotCounted()
    {
        var mine = await MemberAsync();
        var other = await MemberAsync();
        await _seeder.AddIncidentAsync(other.AppId, Title(), severity: Severity.Critical);

        var summary = await SummaryAsync(mine.Client);

        summary.GetProperty("openTotal").GetInt32().Should().Be(0);
        summary.GetProperty("openByApplication").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Get_AllSeveritiesPresent_WhenNoneOpen()
    {
        var m = await MemberAsync();

        var summary = await SummaryAsync(m.Client);

        var severities = summary.GetProperty("openBySeverity");
        foreach (var name in new[] { "Critical", "High", "Medium", "Low" })
        {
            severities.GetProperty(name).GetInt32().Should().Be(0);
        }
    }

    [Fact]
    public async Task Get_MttaMttr_ComputedFromTimestamps()
    {
        var m = await MemberAsync();
        var created = DateTimeOffset.UtcNow.AddMinutes(-90);
        await _seeder.AddIncidentAsync(
            m.AppId,
            Title(),
            at: created,
            configure: (i, actor, at) =>
            {
                i.TransitionTo(IncidentStatus.Triaged, actor, null, at.AddMinutes(10));
                i.TransitionTo(IncidentStatus.InProgress, actor, null, at.AddMinutes(20));
                i.TransitionTo(IncidentStatus.Resolved, actor, "Fixed", at.AddMinutes(60));
            });
        await _seeder.AddIncidentAsync(
            m.AppId,
            Title(),
            at: created,
            configure: (i, actor, at) =>
            {
                i.TransitionTo(IncidentStatus.Triaged, actor, null, at.AddMinutes(20));
                i.TransitionTo(IncidentStatus.InProgress, actor, null, at.AddMinutes(25));
                i.TransitionTo(IncidentStatus.Resolved, actor, "Fixed", at.AddMinutes(80));
            });

        var summary = await SettledAsync(m, x => x.GetProperty("trend")[6].GetProperty("reported").GetInt32() == 2);

        summary.GetProperty("mttaMinutes").GetDouble().Should().Be(15);
        summary.GetProperty("mttrMinutes").GetDouble().Should().Be(70);
    }

    [Fact]
    public async Task Get_NoAcknowledged_MttaIsNull()
    {
        var m = await MemberAsync();
        await _seeder.AddIncidentAsync(m.AppId, Title());

        var summary = await SettledAsync(m, x => x.GetProperty("openTotal").GetInt32() == 1);

        summary.GetProperty("mttaMinutes").ValueKind.Should().Be(JsonValueKind.Null);
        summary.GetProperty("mttrMinutes").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_Trend_ZeroFilledPerDay()
    {
        var m = await MemberAsync();
        await _seeder.AddIncidentAsync(m.AppId, Title());

        var trend = (await SettledAsync(m, x => x.GetProperty("openTotal").GetInt32() == 1)).GetProperty("trend").EnumerateArray().ToList();

        trend.Should().HaveCount(7);
        var days = trend.Select(t => t.GetProperty("date").GetString()!).ToList();
        days.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        days[^1].Should().Be(DateTime.UtcNow.ToString("yyyy-MM-dd"));
        trend[^1].GetProperty("reported").GetInt32().Should().Be(1);
        trend[^1].GetProperty("resolved").GetInt32().Should().Be(0);
        trend.Take(6).Should().OnlyContain(t => t.GetProperty("reported").GetInt32() == 0 && t.GetProperty("resolved").GetInt32() == 0);
    }

    [Theory]
    [InlineData("24h", 1)]
    [InlineData("7d", 7)]
    [InlineData("30d", 30)]
    public async Task Get_Range_TrendHasOnePointPerDay(string range, int days)
    {
        var m = await MemberAsync();

        var summary = await SummaryAsync(m.Client, $"?range={range}");

        summary.GetProperty("range").GetString().Should().Be(range);
        summary.GetProperty("trend").GetArrayLength().Should().Be(days);
    }

    [Theory]
    [InlineData("1h")]
    [InlineData("7D")]
    [InlineData("90d")]
    public async Task Get_InvalidRange_Returns422(string range)
    {
        var m = await MemberAsync();

        var response = await m.Client.GetAsync($"{Url}?range={range}");

        response.StatusCode.Should().Be((HttpStatusCode)422);
        (await JsonAsync(response)).GetProperty("errors").TryGetProperty("range", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Get_DefaultRange_Is7d()
    {
        var m = await MemberAsync();

        (await SummaryAsync(m.Client)).GetProperty("range").GetString().Should().Be("7d");
    }

    [Fact]
    public async Task Get_CacheMiss_RepopulatesWithFiveSecondTtl()
    {
        var m = await MemberAsync();
        var key = await CacheKeyAsync(m);
        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        await redis.KeyDeleteAsync(key);

        await SummaryAsync(m.Client);

        (await redis.KeyExistsAsync(key)).Should().BeTrue();
        (await redis.KeyTimeToLiveAsync(key)).Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Get_WithinTtl_DoesNotQuerySql()
    {
        var m = await MemberAsync();
        await SummaryAsync(m.Client);
        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var key = await CacheKeyAsync(m);

        // SQL says 0 open; a marker planted in the cache can only come back if the second call is served from Redis.
        var cached = (string)(await redis.StringGetAsync(key))!;
        await redis.StringSetAsync(key, cached.Replace("\"OpenTotal\":0", "\"OpenTotal\":42"), TimeSpan.FromSeconds(5));
        var second = await SummaryAsync(m.Client);

        second.GetProperty("openTotal").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task Get_AfterKeyExpired_Recomputes()
    {
        var m = await MemberAsync();
        await SummaryAsync(m.Client);
        await _seeder.AddIncidentAsync(m.AppId, Title(), severity: Severity.Critical);
        await factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().KeyDeleteAsync(await CacheKeyAsync(m));

        var summary = await SummaryAsync(m.Client);

        summary.GetProperty("openTotal").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Get_RedisUnavailable_FallsBackToSql()
    {
        // The provider is exercised directly: sign-in sync (UserDirectory) also needs Redis, so no authenticated request survives a dead Redis.
        var m = await MemberAsync();
        await _seeder.AddIncidentAsync(m.AppId, Title(), severity: Severity.Critical);
        var teamId = await TeamIdAsync(m.AppId);
        using var dead = ConnectionMultiplexer.Connect("127.0.0.1:1,abortConnect=false,connectTimeout=300,syncTimeout=300,asyncTimeout=300");
        using var scope = factory.Services.CreateScope();
        var provider = new RedisDashboardSummaryProvider(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(), dead, TimeProvider.System, NullLogger<RedisDashboardSummaryProvider>.Instance);

        var summary = await provider.GetAsync([teamId], DashboardRange.D7, CancellationToken.None);

        summary.OpenTotal.Should().Be(1);
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> TeamIdAsync(Guid appId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Applications.FindAsync(appId))!.OwningTeamId;
    }

    private async Task<RedisKey> CacheKeyAsync(Member m) =>
        RedisDashboardSummaryProvider.CacheKey([await TeamIdAsync(m.AppId)], DashboardRange.D7);
}
