namespace IncidentHub.Application.Abstractions;

/// <summary>A stored response that is replayed when the same <c>Idempotency-Key</c> is sent again.</summary>
public sealed record IdempotentResponse(int Status, string? Location, string Body);

/// <summary>
/// What the store knows about a key: the hash of the request that claimed it and, once finished, its response.
/// <see cref="Response"/> is null while the first request is still running; a null <see cref="RequestHash"/> marks an entry written before hashing existed.
/// </summary>
public sealed record IdempotencyEntry(string? RequestHash, IdempotentResponse? Response);

public interface IIdempotencyStore
{
    /// <summary>Atomically claims <paramref name="key"/> for a request with <paramref name="requestHash"/>. False when it is already claimed or completed.</summary>
    Task<bool> TryBeginAsync(string key, string requestHash, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>The pending or completed entry, or null when the key is unknown.</summary>
    Task<IdempotencyEntry?> GetAsync(string key, CancellationToken cancellationToken);

    Task CompleteAsync(string key, string requestHash, IdempotentResponse response, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>Drops an unfinished claim so the client can retry after a failure.</summary>
    Task ReleaseAsync(string key, CancellationToken cancellationToken);
}
