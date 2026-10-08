using IncidentHub.Domain.Users;

namespace IncidentHub.Domain.Teams;

/// <summary>Join row (TeamId, UserId) with the member's role in that team. Composite key, so no <c>Entity.Id</c>.</summary>
public sealed class TeamMember
{
    private TeamMember()
    {
    }

    public Guid TeamId { get; private set; }

    public Guid UserId { get; private set; }

    public UserRole Role { get; private set; }

    public static TeamMember Create(Guid teamId, Guid userId, UserRole role) =>
        new() { TeamId = teamId, UserId = userId, Role = role };
}
