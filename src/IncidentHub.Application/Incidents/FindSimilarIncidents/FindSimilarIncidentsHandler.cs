using FluentValidation;
using FluentValidation.Results;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Incidents.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Incidents.FindSimilarIncidents;

public sealed class FindSimilarIncidentsHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IAppTeamReader teams,
    ISimilarIncidentSearch search) : IRequestHandler<FindSimilarIncidentsQuery, IReadOnlyList<IncidentSummaryDto>>
{
    public const int MinSearchLength = 3;
    public const int MaxResults = 5;

    public async Task<IReadOnlyList<IncidentSummaryDto>> Handle(FindSimilarIncidentsQuery request, CancellationToken cancellationToken)
    {
        var isActive = await db.Applications
            .AsNoTracking()
            .Where(a => a.Id == request.ApplicationId)
            .Select(a => (bool?)a.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        if (isActive is not true
            || !await teams.IsMemberAsync(currentUser.UserId, request.ApplicationId, cancellationToken))
        {
            throw new ValidationException(
                [new ValidationFailure(nameof(request.ApplicationId), IncidentMessages.ApplicationUnavailable)]);
        }

        // Called while the user types: too little text is not worth a search.
        var title = request.Title.Trim();
        if (title.Length < MinSearchLength)
        {
            return [];
        }

        var ids = await search.FindOpenAsync(request.ApplicationId, title, MaxResults, cancellationToken);
        if (ids.Count == 0)
        {
            return [];
        }

        var idArray = ids.ToArray();
        var rows = await IncidentProjections
            .SummaryRows(db, db.Incidents.Where(i => idArray.Contains(i.Id)), IncidentPermissions.IsResponder(currentUser.ToActor()))
            .ToListAsync(cancellationToken);

        // The query above has no order; restore the rank order of the search.
        var rank = ids.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        return rows.OrderBy(r => rank[r.Id]).Select(IncidentProjections.ToSummary).ToList();
    }
}
