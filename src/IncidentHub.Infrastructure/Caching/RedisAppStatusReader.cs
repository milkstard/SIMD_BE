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
/// Derived application status: Redis first, SQL on a miss or when Redis fails. Entries are overwritten after every relevant
/// incident change (<see cref="RefreshAsync"/>); the 60 s TTL heals changes made outside the API. The Worker's only incident change (an SLA breach) does not alter the derived status.
/// Cache errors are logged and never reach the caller.
/// </summary>
public sealed class RedisAppStatusReader(AppDbContext db, IConnectionMultiplexer redis, ILogger<RedisAppStatusReader> logger) : IAppStatusReader
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public async Task<IReadOnlyList<AppStatusItemDto>> GetAsync(IReadOnlyCollection<Guid> applicationIds, CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("appstatus.get");
        var ids = applicationIds.Distinct().ToList();
        var found = new Dictionary<Guid, ComputedAppStatus>();

        if (ids.Count > 0)
        {
            try
            {
                var values = await redis.GetDatabase().StringGetAsync(ids.Select(Key).ToArray());
                for (var i = 0; i < ids.Count; i++)
                {
                    if (values[i].HasValue && TryParse(values[i], out var cached))
                    {
                        found[ids[i]] = cached;
                    }
                }
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                logger.LogWarning(ex, "App status cache read failed; falling back to SQL");
            }
        }

        var missing = ids.Where(id => !found.ContainsKey(id)).ToList();
        activity?.SetTag("cache.hit", found.Count);
        activity?.SetTag("cache.miss", missing.Count);

        if (missing.Count > 0)
        {
            var computed = await AppStatusComputation.ComputeAsync(db, missing, cancellationToken);
            foreach (var (id, status) in computed)
            {
                found[id] = status;
                // Never overwrite: a refresh may have stored a fresher value since the read above.
                await TryWriteAsync(id, status, When.NotExists);
            }
        }

        return found.Values.Select(v => v.Item).ToList();
    }

    public async Task<AppStatusRefresh?> RefreshAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("appstatus.refresh");

        ComputedAppStatus? previous = null;
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(Key(applicationId));
            if (value.HasValue && TryParse(value, out var cached))
            {
                previous = cached;
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "App status cache read failed during refresh");
        }

        var computed = await AppStatusComputation.ComputeAsync(db, [applicationId], cancellationToken);
        if (!computed.TryGetValue(applicationId, out var current))
        {
            await TryDeleteAsync(applicationId);
            return null;
        }

        await TryWriteAsync(applicationId, current);
        return new AppStatusRefresh(current.OwningTeamId, previous?.Item.Status, current.Item);
    }

    private async Task TryWriteAsync(Guid applicationId, ComputedAppStatus status, When when = When.Always)
    {
        try
        {
            await redis.GetDatabase().StringSetAsync(Key(applicationId), JsonSerializer.Serialize(status, Json), Ttl, when);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "App status cache write failed for application {ApplicationId}", applicationId);
            await TryDeleteAsync(applicationId);
        }
    }

    private async Task TryDeleteAsync(Guid applicationId)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(Key(applicationId));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "App status cache entry for application {ApplicationId} could not be removed; the TTL will expire it", applicationId);
        }
    }

    private static bool TryParse(RedisValue value, out ComputedAppStatus status)
    {
        try
        {
            status = JsonSerializer.Deserialize<ComputedAppStatus>((string)value!, Json)!;
            return status is not null;
        }
        catch (JsonException)
        {
            status = null!;
            return false;
        }
    }

    private static RedisKey Key(Guid applicationId) => $"appstatus:{applicationId:N}";
}
