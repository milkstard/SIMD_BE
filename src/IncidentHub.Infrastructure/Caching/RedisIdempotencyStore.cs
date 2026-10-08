using System.Text.Json;
using IncidentHub.Application.Abstractions;
using StackExchange.Redis;

namespace IncidentHub.Infrastructure.Caching;

/// <summary>
/// Redis-backed <see cref="IIdempotencyStore"/>. A key is claimed with <c>SET NX</c> (value <c>pending</c>) and later
/// overwritten with the JSON of the completed response; callers must scope keys per user and route.
/// </summary>
public sealed class RedisIdempotencyStore(IConnectionMultiplexer redis) : IIdempotencyStore
{
    private const string Pending = "pending";

    private static RedisKey Key(string key) => $"idem:{key}";

    public async Task<bool> TryBeginAsync(string key, TimeSpan ttl, CancellationToken cancellationToken) =>
        await redis.GetDatabase().StringSetAsync(Key(key), Pending, ttl, When.NotExists);

    public async Task<IdempotentResponse?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(key));
        return !value.HasValue || (string?)value == Pending
            ? null
            : JsonSerializer.Deserialize<IdempotentResponse>((string)value!);
    }

    public async Task CompleteAsync(string key, IdempotentResponse response, TimeSpan ttl, CancellationToken cancellationToken) =>
        await redis.GetDatabase().StringSetAsync(Key(key), JsonSerializer.Serialize(response), ttl);

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken) =>
        await redis.GetDatabase().KeyDeleteAsync(Key(key));
}
