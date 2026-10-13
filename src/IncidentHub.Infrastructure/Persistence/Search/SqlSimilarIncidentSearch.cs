using IncidentHub.Application.Abstractions;
using IncidentHub.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Infrastructure.Persistence.Search;

/// <summary>Full-text similarity on <c>Incidents.Title</c> (index from migration AddIncidents); SQL Server specific, hence not in Application.</summary>
public sealed class SqlSimilarIncidentSearch(AppDbContext db) : ISimilarIncidentSearch
{
    public async Task<IReadOnlyList<Guid>> FindOpenAsync(Guid applicationId, string title, int limit, CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("incident.similar_search");
        activity?.SetTag("application.id", applicationId);

        // Interpolated, so every value becomes a SqlParameter. No top_n_by_rank: it would cut before the application/status filter.
        return await db.Database
            .SqlQuery<Guid>($"""
                SELECT TOP ({limit}) i.Id AS Value
                FROM FREETEXTTABLE(dbo.Incidents, Title, {title}) AS ft
                JOIN dbo.Incidents AS i ON i.Id = ft.[KEY]
                WHERE i.ApplicationId = {applicationId} AND i.Status <> 'Closed'
                ORDER BY ft.RANK DESC, i.CreatedAt DESC
                """)
            .ToListAsync(cancellationToken);
    }
}
