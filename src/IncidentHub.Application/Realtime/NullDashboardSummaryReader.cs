using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Dashboard.Dtos;

namespace IncidentHub.Application.Realtime;

/// <summary>Stands in until BE-11 supplies the real summary: <c>DashboardChanged</c> is simply not sent.</summary>
public sealed class NullDashboardSummaryReader : IDashboardSummaryReader
{
    public Task<DashboardSummaryDto?> GetAsync(Guid teamId, CancellationToken cancellationToken) =>
        Task.FromResult<DashboardSummaryDto?>(null);
}
