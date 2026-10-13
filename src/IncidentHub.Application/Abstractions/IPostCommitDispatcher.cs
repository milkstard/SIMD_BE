using IncidentHub.Domain.Common;

namespace IncidentHub.Application.Abstractions;

/// <summary>
/// Receives the domain events of a save that has already committed, so they can be pushed to clients. Delivery is best-effort:
/// implementations may throw, the caller logs and carries on. The Api implements it with the SignalR hub context; the Worker registers a no-op.
/// </summary>
public interface IPostCommitDispatcher
{
    Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken);
}
