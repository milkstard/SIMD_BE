using StackExchange.Redis;

namespace IncidentHub.Infrastructure.Realtime;

/// <summary>
/// What the Worker tells the API instances after it commits a change: ids and field names only. The receiver re-reads the incident and
/// filters per group as for a local commit, so no incident content (and no internal comment) ever travels on the channel.
/// </summary>
/// <param name="MessageId">Lets the API instances agree on one relayer per message.</param>
/// <param name="Origin">The process that published it (<see cref="ProcessOrigin"/>).</param>
public sealed record IncidentChangeMessage(
    Guid MessageId,
    Guid Origin,
    Guid IncidentId,
    IReadOnlyList<string> ChangedFields,
    Guid? PreviousApplicationId,
    DateTimeOffset OccurredAt);

/// <summary>Identifies this process on the change channel, so a process never relays what it published itself.</summary>
public sealed class ProcessOrigin
{
    public Guid Id { get; } = Guid.NewGuid();
}

public static class ChangeChannel
{
    public const string Name = "incidenthub:changes";

    public static RedisChannel Channel => RedisChannel.Literal(Name);

    /// <summary>Key one API instance sets to take a message; the TTL only has to outlast the delivery to every instance.</summary>
    public static RedisKey ClaimKey(Guid messageId) => $"changes:claim:{messageId:N}";

    public static readonly TimeSpan ClaimTtl = TimeSpan.FromSeconds(30);
}
