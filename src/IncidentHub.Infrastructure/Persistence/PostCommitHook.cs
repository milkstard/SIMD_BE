using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Infrastructure.Persistence;

/// <summary>
/// Hands the domain events of a committed save to <see cref="IPostCommitDispatcher"/>. The write is already durable and delivery is
/// best-effort (API-CONTRACT §7), so a failure or a timeout is logged and never rethrown, and a slow backplane cannot hold the response.
/// </summary>
public sealed class PostCommitHook(IPostCommitDispatcher dispatcher, TimeProvider time, ILogger<PostCommitHook> logger)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public async Task RunAsync(IReadOnlyList<IDomainEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        using var activity = InfrastructureTelemetry.Source.StartActivity("post_commit.dispatch");
        activity?.SetTag("events.count", events.Count);

        // Deliberately not tied to the request's token: the request may end while the write stays committed.
        // The timeout cancels the dispatch itself, so a stalled backplane or database does not leave work holding a connection.
        using var timeout = new CancellationTokenSource(Timeout, time);
        try
        {
            await dispatcher.DispatchAsync(events, timeout.Token).WaitAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            foreach (var e in events)
            {
                logger.LogWarning(ex, "Broadcast of {EventType} for incident {IncidentId} failed", e.GetType().Name, IncidentIdOf(e));
            }
        }
    }

    private static Guid? IncidentIdOf(IDomainEvent e) => e switch
    {
        IncidentReported x => x.IncidentId,
        IncidentTransitioned x => x.IncidentId,
        IncidentAssigned x => x.IncidentId,
        IncidentUpdated x => x.IncidentId,
        CommentAdded x => x.IncidentId,
        AttachmentAdded x => x.IncidentId,
        _ => null,
    };
}
