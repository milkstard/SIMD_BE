using System.Text.Json;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Infrastructure.Outbox;
using IncidentHub.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace IncidentHub.Infrastructure.Realtime;

/// <summary>
/// The Worker's post-commit dispatcher: tells the API instances which incidents changed (<see cref="ChangeChannel"/>). Only
/// <see cref="IncidentUpdated"/> is relayed (an SLA breach); the other events do not change anything a client shows. The write is already
/// durable, so a Redis failure is logged and never reaches the caller.
/// </summary>
public sealed class RedisChangePublisher(IConnectionMultiplexer redis, ProcessOrigin origin, ILogger<RedisChangePublisher> logger)
    : IPostCommitDispatcher
{
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        var updates = events.OfType<IncidentUpdated>().ToList();
        if (updates.Count == 0)
        {
            return;
        }

        using var activity = InfrastructureTelemetry.Source.StartActivity("changes.publish");
        activity?.SetTag("events.count", updates.Count);

        foreach (var update in updates)
        {
            var message = new IncidentChangeMessage(
                Guid.NewGuid(), origin.Id, update.IncidentId, update.ChangedFields, update.PreviousApplicationId, update.OccurredAt);
            try
            {
                await redis.GetSubscriber()
                    .PublishAsync(ChangeChannel.Channel, JsonSerializer.Serialize(message, OutboxJson.Options))
                    .WaitAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                logger.LogWarning(ex, "Change of incident {IncidentId} could not be published; API clients see it on their next read", update.IncidentId);
            }
        }
    }
}
