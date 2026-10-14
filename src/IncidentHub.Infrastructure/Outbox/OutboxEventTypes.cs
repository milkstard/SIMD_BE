using System.Text.Json;
using IncidentHub.Domain.Common;

namespace IncidentHub.Infrastructure.Outbox;

/// <summary>Maps <see cref="OutboxMessage.Type"/> (the event's simple class name) back to its record. Built once by scanning the Domain assembly.</summary>
public sealed class OutboxEventTypes
{
    private readonly Dictionary<string, Type> _types = typeof(IDomainEvent).Assembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(t))
        .ToDictionary(t => t.Name, StringComparer.Ordinal);

    public bool TryDeserialize(OutboxMessage message, out IDomainEvent? domainEvent)
    {
        ArgumentNullException.ThrowIfNull(message);
        domainEvent = null;

        if (!_types.TryGetValue(message.Type, out var type))
        {
            return false;
        }

        try
        {
            domainEvent = JsonSerializer.Deserialize(message.Payload, type, OutboxJson.Options) as IDomainEvent;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return false;
        }

        return domainEvent is not null;
    }
}
