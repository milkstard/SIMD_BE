using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Apps;

public sealed class AppTeamMember : Entity
{
    private AppTeamMember()
    {
    }

    public Guid ApplicationId { get; private set; }

    public Guid UserId { get; private set; }

    public AppTeamRole TeamRole { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public static AppTeamMember Create(Guid applicationId, Guid userId, AppTeamRole teamRole, DateTimeOffset now) =>
        new()
        {
            ApplicationId = applicationId,
            UserId = userId,
            TeamRole = teamRole,
            AddedAt = now,
        };
}
