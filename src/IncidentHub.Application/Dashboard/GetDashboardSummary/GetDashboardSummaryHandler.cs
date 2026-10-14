using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Dashboard.Dtos;
using MediatR;

namespace IncidentHub.Application.Dashboard.GetDashboardSummary;

/// <summary>The summary of the applications owned by the caller's teams (Admin included).</summary>
public sealed class GetDashboardSummaryHandler(IAppTeamReader teams, IDashboardSummaryProvider summaries, ICurrentUser user)
    : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryDto>
{
    public async Task<DashboardSummaryDto> Handle(GetDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        DashboardRanges.TryParse(request.Range, out var range);
        var teamIds = await teams.GetTeamIdsForUserAsync(user.UserId, cancellationToken);
        return await summaries.GetAsync(teamIds, range, cancellationToken);
    }
}
