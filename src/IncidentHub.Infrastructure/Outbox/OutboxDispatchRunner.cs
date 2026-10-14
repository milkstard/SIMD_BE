using IncidentHub.Application.Outbox;
using IncidentHub.Infrastructure.Persistence;
using IncidentHub.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IncidentHub.Infrastructure.Outbox;

/// <summary>
/// One pass of the outbox dispatcher: claims a batch with <c>UPDLOCK, READPAST</c>, hands each message to its handlers and records the
/// outcome, all in one transaction. A crash before the commit rolls the batch back and releases the locks, so rows are redelivered:
/// delivery is at-least-once. The Worker calls it on a loop; it lives here so integration tests can run a single pass.
/// <para>
/// Handlers run in a scope of their own per message, so they never share this class's <see cref="AppDbContext"/> or its open
/// transaction. The locks are held for at most <see cref="OutboxDispatcherOptions.PassBudget"/> plus one
/// <see cref="OutboxDispatcherOptions.HandlerTimeout"/>.
/// </para>
/// </summary>
public sealed class OutboxDispatchRunner(
    AppDbContext db,
    OutboxEventTypes eventTypes,
    IServiceScopeFactory scopes,
    IOptions<OutboxDispatcherOptions> options,
    TimeProvider time,
    ILogger<OutboxDispatchRunner> logger) : IOutboxDispatchRunner
{
    private readonly OutboxDispatcherOptions _options = options.Value;

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("outbox.dispatch");
        var started = time.GetTimestamp();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var batchSize = _options.BatchSize;
        var maxAttempts = _options.MaxAttempts;
        var now = time.GetUtcNow();

        // UPDLOCK + READPAST: a second instance skips the rows this one holds instead of waiting for them.
        // The index is keyed on ProcessedAt, so ordering by OccurredAt makes SQL Server read (and lock) every eligible row, not only
        // the TOP batch: replicas never double-process, but they take turns instead of splitting the backlog.
        var claimed = await db.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT TOP ({batchSize}) * FROM OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE ProcessedAt IS NULL AND Attempts < {maxAttempts} AND (NextAttemptAt IS NULL OR NextAttemptAt <= {now})
                ORDER BY OccurredAt, Id
                """)
            .ToListAsync(cancellationToken);

        activity?.SetTag("outbox.claimed", claimed.Count);

        foreach (var message in claimed)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (time.GetElapsedTime(started) >= _options.PassBudget)
            {
                logger.LogInformation("Outbox pass stopped after its {PassBudget} budget; the rest wait for the next pass", _options.PassBudget);
                break;
            }

            try
            {
                await DispatchAsync(message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The host is stopping. The message in flight stays untouched (redelivered later).
                break;
            }
        }

        // Outcomes already recorded are always saved, whatever the host token says: the handlers have run, so rolling back would
        // resend them on the next start. The SQL command timeout bounds this save.
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return claimed.Count;
    }

    private async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("outbox.handle");
        activity?.SetTag("outbox.id", message.Id);
        activity?.SetTag("outbox.type", message.Type);

        if (!eventTypes.TryDeserialize(message, out var domainEvent) || domainEvent is null)
        {
            message.Park("Unknown event type or unreadable payload.", _options.MaxAttempts);
            logger.LogError("Outbox message {OutboxMessageId} of type {EventType} cannot be read and is parked", message.Id, message.Type);
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handlers = scope.ServiceProvider.GetServices<IOutboxMessageHandler>()
                .Where(h => string.Equals(h.EventType, message.Type, StringComparison.Ordinal))
                .ToList();

            if (handlers.Count == 0)
            {
                // Nothing wants this type yet.
                message.MarkProcessed(time.GetUtcNow());
                logger.LogInformation("Outbox message {OutboxMessageId} of type {EventType} has no handler and is marked processed", message.Id, message.Type);
                return;
            }

            timeout.CancelAfter(_options.HandlerTimeout);

            foreach (var handler in handlers)
            {
                await handler.HandleAsync(domainEvent, message.Id, timeout.Token);
            }

            message.MarkProcessed(time.GetUtcNow());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            // Not the host (rethrown above): the handlers outran HandlerTimeout, which counts as a failed attempt.
            RecordFailure(message, $"Handler timed out after {_options.HandlerTimeout}.", exception);
        }
        catch (NonRetryableOutboxException exception)
        {
            message.Park(Describe(exception), _options.MaxAttempts);
            logger.LogError(
                exception,
                "Outbox message {OutboxMessageId} of type {EventType} failed for good and is parked",
                message.Id,
                message.Type);
        }
        catch (Exception exception)
        {
            RecordFailure(message, Describe(exception), exception);
        }
    }

    private void RecordFailure(OutboxMessage message, string error, Exception exception)
    {
        var now = time.GetUtcNow();
        message.MarkFailed(error, OutboxBackoff.NextAttemptAt(message.Attempts + 1, now, _options, Random.Shared));
        logger.LogWarning(
            exception,
            "Outbox message {OutboxMessageId} of type {EventType} failed on attempt {Attempts}",
            message.Id,
            message.Type,
            message.Attempts);

        if (message.Attempts >= _options.MaxAttempts)
        {
            logger.LogError(
                "Outbox message {OutboxMessageId} of type {EventType} reached {MaxAttempts} attempts and is parked",
                message.Id,
                message.Type,
                _options.MaxAttempts);
        }
    }

    private static string Describe(Exception exception) => $"{exception.GetType().Name}: {exception.Message}";
}
