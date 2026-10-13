using System.Text.Json;
using IncidentHub.Application.Abstractions;
using IncidentHub.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Infrastructure.Persistence.Search;

/// <summary>Full-text search on <c>Incidents.Title</c> and <c>Description</c> (index from migration AddIncidents); SQL Server specific, hence not in Application.</summary>
public sealed class SqlIncidentTextSearch(AppDbContext db) : IIncidentTextSearch
{
    public async Task<IReadOnlyList<Guid>> FindIdsAsync(
        string text,
        IReadOnlyCollection<Guid> applicationIds,
        int limit,
        CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("incident.text_search");
        activity?.SetTag("application.count", applicationIds.Count);

        // Interpolated, so every value becomes a SqlParameter; the id list travels as one JSON parameter, never as concatenated SQL.
        var applicationIdsJson = JsonSerializer.Serialize(applicationIds);

        return await db.Database
            .SqlQuery<Guid>($"""
                SELECT TOP ({limit}) i.Id AS Value
                FROM FREETEXTTABLE(dbo.Incidents, (Title, Description), {text}) AS ft
                JOIN dbo.Incidents AS i ON i.Id = ft.[KEY]
                WHERE i.ApplicationId IN (SELECT CAST([value] AS uniqueidentifier) FROM OPENJSON({applicationIdsJson}))
                ORDER BY ft.RANK DESC, i.CreatedAt DESC
                """)
            .ToListAsync(cancellationToken);
    }
}
