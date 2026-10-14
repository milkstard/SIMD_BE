using IncidentHub.Application.Dashboard.Dtos;
using IncidentHub.Domain.Apps;

namespace IncidentHub.Application.Abstractions;

public interface IAppStatusReader
{
    /// <summary>Statuses of the active applications among <paramref name="applicationIds"/>: the cached value, else computed from SQL. Unknown or inactive ids are absent.</summary>
    Task<IReadOnlyList<AppStatusItemDto>> GetAsync(IReadOnlyCollection<Guid> applicationIds, CancellationToken cancellationToken);

    /// <summary>
    /// Recomputes one application from SQL and overwrites its cache entry (the invalidation after an incident change).
    /// Returns null for an unknown or inactive application. Cache errors are logged, never thrown.
    /// </summary>
    Task<AppStatusRefresh?> RefreshAsync(Guid applicationId, CancellationToken cancellationToken);
}

/// <param name="Previous">The status held in the cache before the refresh; null when nothing was cached.</param>
public sealed record AppStatusRefresh(Guid OwningTeamId, AppStatus? Previous, AppStatusItemDto Current);
