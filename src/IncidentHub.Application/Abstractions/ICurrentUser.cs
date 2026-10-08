using IncidentHub.Domain.Users;

namespace IncidentHub.Application.Abstractions;

public interface ICurrentUser
{
    /// <summary>Internal user key (<c>User.Id</c>), not the Entra object id.</summary>
    Guid UserId { get; }

    string EntraObjectId { get; }

    string DisplayName { get; }

    string Email { get; }

    IReadOnlySet<UserRole> Roles { get; }

    bool IsInRole(UserRole role);

    Actor ToActor();
}
