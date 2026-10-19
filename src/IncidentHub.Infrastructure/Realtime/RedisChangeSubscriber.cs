using System.Text.Json;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Infrastructure.Outbox;
using IncidentHub.Infrastructure.Persistence;
using IncidentHub.Infrastructure.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace IncidentHub.Infrastructure.Realtime;

/// <summary>
/// API side of the change relay: for every message the Worker publishes on <see cref="ChangeChannel"/>, hands the matching
/// <see cref="IncidentUpdated"/> to the host's <see cref="IPostCommitDispatcher"/> (the SignalR one), exactly as a local commit would.
/// The SignalR Redis backplane already carries a send to the clients of every instance, so each message is claimed in Redis and only the
/// instance that wins relays it; otherwise every client would get it once per instance. Delivery is best-effort like the broadcasts: a
/// missed message is healed by the cache TTL and the client's refetch on reconnect.
/// </summary>
public sealed class RedisChangeSubscriber(
    IConnectionMultiplexer redis,
    IPostCommitDispatcher dispatcher,
    ProcessOrigin origin,
    TimeProvider time,
    ILogger<RedisChangeSubscriber> logger) : BackgroundService
{
    public static readonly TimeSpan ResubscribeDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ChannelMessageQueue? queue = null;
            try
            {
                queue = await redis.GetSubscriber().SubscribeAsync(ChangeChannel.Channel);
                await foreach (var message in queue.WithCancellation(stoppingToken))
                {
                    await HandleAsync(message.Message, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Any failure, not only Redis ones: an unhandled exception here would stop the whole API host.
                logger.LogWarning(ex, "Change relay subscription failed; retrying in {DelaySeconds} s", ResubscribeDelay.TotalSeconds);
            }
            finally
            {
                await UnsubscribeAsync(queue);
            }

            try
            {
                await Task.Delay(ResubscribeDelay, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Drops the subscription so a resubscribe does not leave an unread queue buffering. Best-effort.</summary>
    private async Task UnsubscribeAsync(ChannelMessageQueue? queue)
    {
        if (queue is null)
        {
            return;
        }

        try
        {
            await queue.UnsubscribeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Change relay unsubscribe failed");
        }
    }

    /// <summary>Relays one message. Never throws except when the host is stopping: a bad message must not end the subscription.</summary>
    public async Task HandleAsync(RedisValue payload, CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("changes.relay");

        var message = Parse(payload);
        if (message is null)
        {
            activity?.SetTag("changes.outcome", "malformed");
            logger.LogWarning("Ignored a malformed message on the change channel ({Length} characters)", payload.Length());
            return;
        }

        if (message.Origin == origin.Id)
        {
            activity?.SetTag("changes.outcome", "own");
            return;
        }

        try
        {
            if (!await redis.GetDatabase().StringSetAsync(ChangeChannel.ClaimKey(message.MessageId), origin.Id.ToString("N"), ChangeChannel.ClaimTtl, When.NotExists))
            {
                activity?.SetTag("changes.outcome", "claimed-elsewhere");
                return;
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // Without the claim two instances could both relay; a skipped message is the cheaper failure.
            activity?.SetTag("changes.outcome", "claim-failed");
            logger.LogWarning(ex, "Change of incident {IncidentId} not relayed: the relay claim failed", message.IncidentId);
            return;
        }

        // Like PostCommitHook: a stalled dispatch must not hold the subscription.
        using var timeout = new CancellationTokenSource(PostCommitHook.Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var updated = new IncidentUpdated(message.IncidentId, message.ChangedFields, null, message.OccurredAt, message.PreviousApplicationId);
            await dispatcher.DispatchAsync([updated], linked.Token).WaitAsync(linked.Token);
            activity?.SetTag("changes.outcome", "relayed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetTag("changes.outcome", "failed");
            logger.LogWarning(ex, "Relay of the change of incident {IncidentId} failed", message.IncidentId);
        }
    }

    private static IncidentChangeMessage? Parse(RedisValue payload)
    {
        try
        {
            var message = JsonSerializer.Deserialize<IncidentChangeMessage>((string)payload!, OutboxJson.Options);
            return message is null || message.IncidentId == Guid.Empty || message.MessageId == Guid.Empty || message.ChangedFields is null
                ? null
                : message;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
