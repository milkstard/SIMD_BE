using IncidentHub.Application.Dashboard.Dtos;

namespace IncidentHub.Application.Abstractions;

public interface IDashboardSummaryReader
{
    /// <summary>The summary pushed with <c>DashboardChanged</c>; null means there is nothing to send.</summary>
    Task<DashboardSummaryDto?> GetAsync(Guid teamId, CancellationToken cancellationToken);
}
