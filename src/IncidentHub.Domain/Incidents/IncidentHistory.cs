using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents;

/// <summary>One append-only audit row. Created only by <see cref="Incident"/>; never updated or deleted.</summary>
public sealed class IncidentHistory : Entity
{
    public const int FieldMaxLength = 50;
    public const int NoteMaxLength = 1_000;

    private IncidentHistory()
    {
    }

    public Guid IncidentId { get; private set; }

    public string Field { get; private set; } = string.Empty;

    public string? OldValue { get; private set; }

    public string? NewValue { get; private set; }

    public string? Note { get; private set; }

    /// <summary>The user who made the change; <c>null</c> when the system did (SLA job, webhook).</summary>
    public Guid? ActorId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static IncidentHistory Create(
        Guid incidentId,
        string field,
        string? oldValue,
        string? newValue,
        string? note,
        Guid? actorId,
        DateTimeOffset at) =>
        new()
        {
            IncidentId = incidentId,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue,
            Note = note,
            ActorId = actorId,
            CreatedAt = at,
        };
}
