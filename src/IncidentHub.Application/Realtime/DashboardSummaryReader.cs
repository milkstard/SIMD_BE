using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Dashboard;
using IncidentHub.Application.Dashboard.Dtos;

namespace IncidentHub.Application.Realtime;

/// <summary>
/// The <c>DashboardChanged</c> payload: the default <c>7d</c> summary of one team. It runs right after a commit, so it recomputes
/// instead of trusting a cache entry that may predate the change.
/// </summary>
public sealed class DashboardSummaryReader(IDashboardSummaryProvider summaries) : IDashboardSummaryReader
{
    public async Task<DashboardSummaryDto?> GetAsync(Guid teamId, CancellationToken cancellationToken) =>
        await summaries.RefreshAsync([teamId], DashboardRange.D7, cancellationToken);
}
