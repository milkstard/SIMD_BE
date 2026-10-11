using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Users;

namespace IncidentHub.Worker.Identity;

/// <summary>
/// The Worker never acts as a signed-in user (.claude/specs/entra-id-authorization.md §12): handlers it runs see the fixed
/// system actor, with no roles, so any user-scoped check fails closed.
/// </summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public Guid UserId => Actor.System.UserId;

    public string EntraObjectId => string.Empty;

    public string DisplayName => "System";

    public string Email => string.Empty;

    public IReadOnlySet<UserRole> Roles => Actor.System.Roles;

    public bool IsInRole(UserRole role) => Actor.System.IsInRole(role);

    public Actor ToActor() => Actor.System;
}
