using MediatR;

namespace IncidentHub.Application.Teams.CreateTeam;

public sealed record CreateTeamCommand(string Name, string Email, string? TeamsChannelUrl) : IRequest<TeamDto>;
