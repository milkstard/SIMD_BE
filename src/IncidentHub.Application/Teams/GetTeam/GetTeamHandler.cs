using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Teams.GetTeam;

public sealed class GetTeamHandler(IAppDbContext db) : IRequestHandler<GetTeamQuery, TeamDto>
{
    public async Task<TeamDto> Handle(GetTeamQuery request, CancellationToken cancellationToken) =>
        await db.Teams.AsNoTracking()
            .Where(t => t.Id == request.Id)
            .Select(TeamDto.Projection)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException($"Team {request.Id} was not found.");
}
