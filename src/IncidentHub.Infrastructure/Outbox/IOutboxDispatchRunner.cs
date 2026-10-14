namespace IncidentHub.Infrastructure.Outbox;

/// <summary>One dispatch pass; the Worker job calls it, tests fake it.</summary>
public interface IOutboxDispatchRunner
{
    /// <summary>Returns the number of rows claimed in this pass.</summary>
    Task<int> RunOnceAsync(CancellationToken cancellationToken);
}
