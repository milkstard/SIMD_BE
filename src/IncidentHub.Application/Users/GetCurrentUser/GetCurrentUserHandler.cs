using IncidentHub.Application.Abstractions;
using MediatR;

namespace IncidentHub.Application.Users.GetCurrentUser;

public sealed class GetCurrentUserHandler(ICurrentUser user, IAppTeamReader teams)
    : IRequestHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public async Task<CurrentUserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var teamIds = await teams.GetTeamIdsForUserAsync(user.UserId, cancellationToken);
        return new CurrentUserDto(
            user.UserId,
            user.DisplayName,
            user.Email,
            user.Roles.Order().ToList(),
            teamIds.Order().ToList());
    }
}
