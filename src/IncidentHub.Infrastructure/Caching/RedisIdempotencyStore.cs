using System.Text.Json;
using IncidentHub.Application.Abstractions;
using StackExchange.Redis;

namespace IncidentHub.Infrastructure.Caching;

/// <summary>
/// Redis-backed <see cref="IIdempotencyStore"/>. A key is claimed with <c>SET NX</c> (a JSON record holding the request hash and no
/// response) and later overwritten with the same record plus the completed response; callers must scope keys per user and route.
/// </summary>
public sealed class RedisIdempotencyStore(IConnectionMultiplexer redis) : IIdempotencyStore
{
    private const string LegacyPending = "pending";

    private static RedisKey Key(string key) => $"idem:{key}";

    public async Task<bool> TryBeginAsync(string key, string requestHash, TimeSpan ttl, CancellationToken cancellationToken) =>
        await redis.GetDatabase().StringSetAsync(Key(key), JsonSerializer.Serialize(new IdempotencyEntry(requestHash, null)), ttl, When.NotExists);

    public async Task<IdempotencyEntry?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(key));
        if (!value.HasValue)
        {
            return null;
        }

        var json = (string)value!;
        return json == LegacyPending
            ? new IdempotencyEntry(null, null)
            : JsonSerializer.Deserialize<IdempotencyEntry>(json);
    }

    public async Task CompleteAsync(string key, string requestHash, IdempotentResponse response, TimeSpan ttl, CancellationToken cancellationToken) =>
        await redis.GetDatabase().StringSetAsync(Key(key), JsonSerializer.Serialize(new IdempotencyEntry(requestHash, response)), ttl);

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken) =>
        await redis.GetDatabase().KeyDeleteAsync(Key(key));
}
