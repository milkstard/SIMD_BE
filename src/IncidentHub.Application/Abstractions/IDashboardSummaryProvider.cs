using IncidentHub.Application.Dashboard;
using IncidentHub.Application.Dashboard.Dtos;

namespace IncidentHub.Application.Abstractions;

public interface IDashboardSummaryProvider
{
    /// <summary>The summary of the given teams: from the cache when fresh, otherwise computed and cached.</summary>
    Task<DashboardSummaryDto> GetAsync(IReadOnlyCollection<Guid> teamIds, DashboardRange range, CancellationToken cancellationToken);

    /// <summary>Computes the summary from SQL and overwrites the cache entry; used after a commit, where a cached value would be stale.</summary>
    Task<DashboardSummaryDto> RefreshAsync(IReadOnlyCollection<Guid> teamIds, DashboardRange range, CancellationToken cancellationToken);
}
