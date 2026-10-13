using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Incidents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Incidents.GetIncident;

public sealed class GetIncidentHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IAppTeamReader teams) : IRequestHandler<GetIncidentQuery, IncidentDetailDto>
{
    public async Task<IncidentDetailDto> Handle(GetIncidentQuery request, CancellationToken cancellationToken)
    {
        if (!IncidentNumber.TryParse(request.Number, out var number))
        {
            throw new NotFoundException(IncidentMessages.IncidentNotFound);
        }

        var value = number.Value;
        var summary = await IncidentProjections
            .SummaryRows(db, db.Incidents.Where(i => i.Number == value), IncidentPermissions.IsResponder(currentUser.ToActor()))
            .FirstOrDefaultAsync(cancellationToken);

        // Unknown and not-visible look the same.
        if (summary is null
            || !await teams.IsMemberAsync(currentUser.UserId, summary.Application.Id, cancellationToken))
        {
            throw new NotFoundException(IncidentMessages.IncidentNotFound);
        }

        var extras = await IncidentProjections.DetailExtras(db, summary.Id).FirstAsync(cancellationToken);
        return IncidentProjections.ToDetail(summary, extras, currentUser.ToActor());
    }
}
