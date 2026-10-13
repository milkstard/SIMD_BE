using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Common;

namespace IncidentHub.Worker.Identity;

/// <summary>The Worker pushes nothing to SignalR clients; the Api owns the hub.</summary>
public sealed class NoOpPostCommitDispatcher : IPostCommitDispatcher
{
    public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken) => Task.CompletedTask;
}
