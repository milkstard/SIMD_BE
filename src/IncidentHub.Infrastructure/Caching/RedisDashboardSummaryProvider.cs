using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Dashboard;
using IncidentHub.Application.Dashboard.Dtos;
using IncidentHub.Infrastructure.Persistence;
using IncidentHub.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace IncidentHub.Infrastructure.Caching;

/// <summary>
/// Dashboard summary: Redis first, SQL on a miss or when Redis fails, 5 s TTL. Concurrent misses are collapsed with a short
/// <c>SET NX</c> guard so a burst runs the query once; a request that loses the guard waits briefly for the winner's entry and
/// computes itself if it does not appear. Cache errors are logged and never reach the caller.
/// </summary>
public sealed class RedisDashboardSummaryProvider(
    AppDbContext db,
    IConnectionMultiplexer redis,
    TimeProvider time,
    ILogger<RedisDashboardSummaryProvider> logger) : IDashboardSummaryProvider
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan GuardTtl = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GuardWait = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan GuardPoll = TimeSpan.FromMilliseconds(50);

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public static string CacheKey(IReadOnlyCollection<Guid> teamIds, DashboardRange range)
    {
        var ids = string.Join(',', teamIds.Distinct().OrderBy(id => id).Select(id => id.ToString("N")));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ids)), 0, 16);
        return $"dashboard:summary:{range.Token()}:{hash}";
    }

    public async Task<DashboardSummaryDto> GetAsync(IReadOnlyCollection<Guid> teamIds, DashboardRange range, CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("dashboard.summary");
        activity?.SetTag("range", range.Token());

        var key = CacheKey(teamIds, range);
        var cached = await TryReadAsync(key);
        if (cached is not null)
        {
            activity?.SetTag("cache.hit", true);
            return cached;
        }

        activity?.SetTag("cache.hit", false);
        var guard = (RedisKey)(key + ":lock");
        var owner = Guid.NewGuid().ToString("N");
        var acquired = await TryAcquireAsync(guard, owner);
        var hasGuard = acquired == true;

        // null = Redis failed: no point waiting for a winner, compute at once.
        if (acquired == false)
        {
            for (var waited = TimeSpan.Zero; waited < GuardWait; waited += GuardPoll)
            {
                await Task.Delay(GuardPoll, cancellationToken);
                cached = await TryReadAsync(key);
                if (cached is not null)
                {
                    return cached;
                }
            }
        }

        try
        {
            var summary = await DashboardSummaryQueries.ComputeAsync(db, teamIds, range, time, cancellationToken);

            // Never overwrite: a post-commit refresh may have stored a fresher value since the read above.
            await TryWriteAsync(key, summary, When.NotExists);
            return summary;
        }
        finally
        {
            if (hasGuard)
            {
                await TryReleaseAsync(guard, owner);
            }
        }
    }

    public async Task<DashboardSummaryDto> RefreshAsync(IReadOnlyCollection<Guid> teamIds, DashboardRange range, CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("dashboard.summary");
        activity?.SetTag("range", range.Token());
        activity?.SetTag("cache.hit", false);

        var summary = await DashboardSummaryQueries.ComputeAsync(db, teamIds, range, time, cancellationToken);
        await TryWriteAsync(CacheKey(teamIds, range), summary);
        return summary;
    }

    private async Task<DashboardSummaryDto?> TryReadAsync(string key)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(key);
            return value.HasValue ? JsonSerializer.Deserialize<DashboardSummaryDto>((string)value!, Json) : null;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or JsonException)
        {
            logger.LogWarning(ex, "Dashboard summary cache read failed; falling back to SQL");
            return null;
        }
    }

    private async Task TryWriteAsync(string key, DashboardSummaryDto summary, When when = When.Always)
    {
        try
        {
            await redis.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(summary, Json), Ttl, when);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Dashboard summary cache write failed");
        }
    }

    private async Task<bool?> TryAcquireAsync(RedisKey guard, string owner)
    {
        try
        {
            return await redis.GetDatabase().StringSetAsync(guard, owner, GuardTtl, When.NotExists);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Dashboard summary guard unavailable; computing without it");
            return null;
        }
    }

    private async Task TryReleaseAsync(RedisKey guard, string owner)
    {
        try
        {
            // Only the owner may delete it; the guard may have expired and been taken by another request.
            await redis.GetDatabase().ScriptEvaluateAsync(
                "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end",
                [guard],
                [owner]);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Dashboard summary guard could not be released; it expires on its own");
        }
    }
}
