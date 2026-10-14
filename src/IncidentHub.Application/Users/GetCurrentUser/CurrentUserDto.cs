using IncidentHub.Domain.Users;

namespace IncidentHub.Application.Users.GetCurrentUser;

/// <summary>The signed-in user. <c>Id</c> is the internal <c>User.Id</c>; <c>Roles</c> come from the token's group mapping.</summary>
public sealed record CurrentUserDto(
    Guid Id,
    string DisplayName,
    string Email,
    IReadOnlyList<UserRole> Roles,
    IReadOnlyList<Guid> TeamIds);
