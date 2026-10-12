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
}
