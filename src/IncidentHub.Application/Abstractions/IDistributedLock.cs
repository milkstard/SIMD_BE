namespace IncidentHub.Application.Abstractions;

/// <summary>Cross-process mutual exclusion for Worker jobs that must run on one instance at a time.</summary>
public interface IDistributedLock
{
    /// <summary>Takes the lock for at most <paramref name="ttl"/>. Returns null when another holder has it; dispose the handle to release.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken);
}
