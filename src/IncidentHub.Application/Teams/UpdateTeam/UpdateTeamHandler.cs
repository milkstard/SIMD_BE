using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Teams.UpdateTeam;

public sealed class UpdateTeamHandler(IAppDbContext db, ILogger<UpdateTeamHandler> logger)
    : IRequestHandler<UpdateTeamCommand, TeamDto>
{
    public async Task<TeamDto> Handle(UpdateTeamCommand request, CancellationToken cancellationToken)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException($"Team {request.Id} was not found.");

        if (!team.RowVersion.AsSpan().SequenceEqual(request.ExpectedRowVersion))
        {
            throw new ConflictException($"Team {request.Id} was modified by someone else.");
        }

        team.Update(request.Name, request.Email, request.TeamsChannelUrl);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Team {TeamId} updated", team.Id);
        return TeamDto.From(team);
    }
}
