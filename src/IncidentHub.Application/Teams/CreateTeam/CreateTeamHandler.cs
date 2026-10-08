using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Teams;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Teams.CreateTeam;

public sealed class CreateTeamHandler(IAppDbContext db, ILogger<CreateTeamHandler> logger)
    : IRequestHandler<CreateTeamCommand, TeamDto>
{
    public async Task<TeamDto> Handle(CreateTeamCommand request, CancellationToken cancellationToken)
    {
        var team = Team.Create(request.Name, request.Email, request.TeamsChannelUrl);
        db.Teams.Add(team);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Team {TeamId} created", team.Id);
        return TeamDto.From(team);
    }
}
