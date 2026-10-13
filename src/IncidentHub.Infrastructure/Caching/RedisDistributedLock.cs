using IncidentHub.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace IncidentHub.Infrastructure.Caching;

/// <summary>
/// Single-key Redis lock: <c>SET key token NX PX ttl</c> to take it, a compare-and-delete script to release it, so a holder
/// whose lock already expired cannot delete the next holder's lock. The ttl bounds the damage of a crashed holder.
/// </summary>
public sealed class RedisDistributedLock(IConnectionMultiplexer redis, ILogger<RedisDistributedLock> logger) : IDistributedLock
{
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    public async Task<IAsyncDisposable?> TryAcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var database = redis.GetDatabase();
        var key = (RedisKey)$"lock:{name}";
        var token = Guid.NewGuid().ToString("N");

        return await database.StringSetAsync(key, token, ttl, When.NotExists)
            ? new Handle(database, key, token, logger)
            : null;
    }

    private sealed class Handle(IDatabase database, RedisKey key, string token, ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await database.ScriptEvaluateAsync(ReleaseScript, [key], [(RedisValue)token]);
            }
            catch (RedisException exception)
            {
                // The work under the lock is done; the ttl frees the lock, so a failed release is not a failed pass.
                logger.LogWarning(exception, "Lock {LockKey} could not be released; it expires on its own", key.ToString());
            }
        }
    }
}
