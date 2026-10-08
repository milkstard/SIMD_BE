namespace IncidentHub.Domain.Users;

/// <summary>Who performed an action. Passed to domain operations such as <c>Incident.TransitionTo</c>.</summary>
public sealed record Actor(Guid UserId, IReadOnlySet<UserRole> Roles)
{
    /// <summary>Fixed identity for Worker-initiated actions (SLA escalation, notifications).</summary>
    public static Actor System { get; } = new(Guid.Empty, new HashSet<UserRole>());

    public bool IsSystem => UserId == Guid.Empty;

    public bool IsInRole(UserRole role) => Roles.Contains(role);
}
