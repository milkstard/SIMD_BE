using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Common;

namespace IncidentHub.Infrastructure.Persistence;

/// <summary>Default when a host registers no dispatcher of its own (the Worker pushes nothing to SignalR; the Api replaces it with the hub dispatcher).</summary>
public sealed class NoOpPostCommitDispatcher : IPostCommitDispatcher
{
    public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken) => Task.CompletedTask;
}
