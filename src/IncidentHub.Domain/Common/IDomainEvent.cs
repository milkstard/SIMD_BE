namespace IncidentHub.Domain.Common;

/// <summary>Something that happened to an aggregate. Carries Ids and values only, so it can be serialized into an outbox payload.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
