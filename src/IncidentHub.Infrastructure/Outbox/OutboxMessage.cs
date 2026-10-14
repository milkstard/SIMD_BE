using System.Text.Json;
using IncidentHub.Domain.Common;

namespace IncidentHub.Infrastructure.Outbox;

/// <summary>
/// A domain event waiting to be dispatched. A persistence type owned by Infrastructure: it is written by
/// <c>AppDbContext</c> in the same save as the change that raised the event, never by a handler.
/// </summary>
public sealed class OutboxMessage
{
    public const int TypeMaxLength = 50;
    public const int ErrorMaxLength = 2000;

    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Earliest time the dispatcher may try again after a failure; null = now.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>Done, or deliberately skipped because nothing handles this type.</summary>
    public void MarkProcessed(DateTimeOffset at) => ProcessedAt = at;

    public void MarkFailed(string error, DateTimeOffset nextAttemptAt)
    {
        Attempts++;
        Error = Truncate(error);
        NextAttemptAt = nextAttemptAt;
    }

    /// <summary>
    /// Stops the retries: <c>Attempts</c> is raised to <paramref name="maxAttempts"/> so the claim query skips the row.
    /// <c>ProcessedAt</c> stays null so the row remains visible to operators.
    /// </summary>
    public void Park(string error, int maxAttempts)
    {
        Attempts = Math.Max(Attempts + 1, maxAttempts);
        Error = Truncate(error);
        NextAttemptAt = null;
    }

    public static OutboxMessage From(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventType = domainEvent.GetType();
        if (eventType.Name.Length > TypeMaxLength)
        {
            throw new InvalidOperationException($"Event type name {eventType.Name} is longer than {TypeMaxLength} characters.");
        }

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = eventType.Name,
            Payload = JsonSerializer.Serialize(domainEvent, eventType, OutboxJson.Options),
            OccurredAt = domainEvent.OccurredAt,
        };
    }

    private static string Truncate(string error) =>
        error.Length <= ErrorMaxLength ? error : error[..ErrorMaxLength];
}
