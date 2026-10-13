using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents;

/// <summary>A comment on an incident. Created only through <see cref="Incident.AddComment"/>.</summary>
public sealed class Comment : Entity
{
    public const int BodyMaxLength = 10_000;

    private Comment()
    {
    }

    public Guid IncidentId { get; private set; }

    public Guid AuthorId { get; private set; }

    /// <summary>Sanitized HTML.</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>Visible to responders only; never returned to Reporters.</summary>
    public bool IsInternal { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? EditedAt { get; private set; }

    internal static Comment Create(Guid incidentId, Guid authorId, string body, bool isInternal, DateTimeOffset at) =>
        new()
        {
            IncidentId = incidentId,
            AuthorId = authorId,
            Body = body,
            IsInternal = isInternal,
            CreatedAt = at,
        };
}
