using IncidentHub.Application.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Incidents.CanWatchIncident;

public sealed class CanWatchIncidentHandler(IAppDbContext db, IAppTeamReader teams)
    : IRequestHandler<CanWatchIncidentQuery, bool>
{
    public async Task<bool> Handle(CanWatchIncidentQuery request, CancellationToken cancellationToken)
    {
        var applicationId = await db.Incidents
            .AsNoTracking()
            .Where(i => i.Id == request.IncidentId)
            .Select(i => (Guid?)i.ApplicationId)
            .FirstOrDefaultAsync(cancellationToken);

        return applicationId is { } id && await teams.IsMemberAsync(request.UserId, id, cancellationToken);
    }
}
