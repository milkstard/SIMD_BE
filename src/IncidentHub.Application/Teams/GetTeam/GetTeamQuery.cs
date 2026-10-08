using MediatR;

namespace IncidentHub.Application.Teams.GetTeam;

public sealed record GetTeamQuery(Guid Id) : IRequest<TeamDto>;
