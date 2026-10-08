namespace IncidentHub.Application.Abstractions;

/// <summary>A stored response that is replayed when the same <c>Idempotency-Key</c> is sent again.</summary>
public sealed record IdempotentResponse(int Status, string? Location, string Body);

public interface IIdempotencyStore
{
    /// <summary>Atomically claims <paramref name="key"/>. False when another request already claimed or completed it.</summary>
    Task<bool> TryBeginAsync(string key, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>The completed response, or null when the key is unknown or still in progress.</summary>
    Task<IdempotentResponse?> GetAsync(string key, CancellationToken cancellationToken);

    Task CompleteAsync(string key, IdempotentResponse response, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>Drops an unfinished claim so the client can retry after a failure.</summary>
    Task ReleaseAsync(string key, CancellationToken cancellationToken);
}
