using MediatR;

namespace IncidentHub.Application.Users.GetCurrentUser;

public sealed record GetCurrentUserQuery : IRequest<CurrentUserDto>;
