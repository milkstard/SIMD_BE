using System.Data;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Infrastructure;

/// <summary>Creates incidents directly in the test database, and waits for full-text indexing (change tracking is asynchronous).</summary>
public sealed class IncidentSeeder(ApiFactory factory)
{
    private static readonly string[] Subjects = ["checkout", "payment", "login", "search", "invoice", "report", "upload", "profile", "catalog", "inventory"];
    private static readonly string[] Problems = ["fails", "times out", "returns errors", "is slow", "crashes", "shows blank page", "loses data", "rejects requests"];
    private static readonly string[] Contexts = ["after deploy", "for new users", "on mobile", "under load", "at midnight", "in Safari", "behind proxy", "after login"];

    private string ConnectionString => factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Sql"]!;

    /// <summary>Adds one incident through the domain; <paramref name="closed"/> moves it to Closed (Invalid).</summary>
    public async Task<Guid> AddAsync(Guid appId, string title, bool closed = false)
    {
        var reporterId = await factory.EnsureUserAsync(Guid.NewGuid().ToString());
        var actor = new Actor(reporterId, new HashSet<UserRole> { UserRole.Responder });
        var now = DateTimeOffset.UtcNow;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var incident = Incident.Report(
            appId, AppEnvironment.Production, Severity.High, title, "<p>Users see an error page.</p>", null, IncidentSource.Manual, actor, now);
        if (closed)
        {
            incident.TransitionTo(IncidentStatus.Closed, actor, null, now, CloseReason.Invalid);
        }

        db.Incidents.Add(incident);
        await db.SaveChangesAsync();
        return incident.Id;
    }

    /// <summary>
    /// Adds one incident through the domain with a chosen reporter, severity, creation time and description,
    /// then lets <paramref name="configure"/> move or assign it before the save. Returns id and display number.
    /// </summary>
    public async Task<(Guid Id, string Number)> AddIncidentAsync(
        Guid appId,
        string title,
        Guid? reporterId = null,
        Severity severity = Severity.High,
        DateTimeOffset? at = null,
        string description = "<p>Users see an error page.</p>",
        Action<Incident, Actor, DateTimeOffset>? configure = null)
    {
        var reporter = reporterId ?? await factory.EnsureUserAsync(Guid.NewGuid().ToString());
        var actor = new Actor(reporter, new HashSet<UserRole> { UserRole.Responder });
        var now = at ?? DateTimeOffset.UtcNow;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var incident = Incident.Report(
            appId, AppEnvironment.Production, severity, title, description, null, IncidentSource.Manual, actor, now);
        configure?.Invoke(incident, actor, now);

        db.Incidents.Add(incident);
        await db.SaveChangesAsync();
        return (incident.Id, new IncidentNumber(incident.Number).ToString());
    }

    /// <summary>Bulk-inserts <paramref name="count"/> open incidents with varied titles (Number comes from the sequence default).</summary>
    public async Task BulkInsertAsync(Guid appId, Guid reporterId, int count, int firstIndex = 0)
    {
        var table = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
        {
            ("Id", typeof(Guid)), ("ApplicationId", typeof(Guid)), ("Environment", typeof(string)), ("Title", typeof(string)),
            ("Description", typeof(string)), ("Severity", typeof(string)), ("Status", typeof(string)), ("Source", typeof(string)),
            ("ReporterId", typeof(Guid)), ("Fingerprint", typeof(string)), ("IsSlaBreached", typeof(bool)),
            ("CreatedAt", typeof(DateTimeOffset)), ("UpdatedAt", typeof(DateTimeOffset)),
        })
        {
            table.Columns.Add(name, type);
        }

        var now = DateTimeOffset.UtcNow;
        for (var n = firstIndex; n < firstIndex + count; n++)
        {
            var id = Guid.NewGuid();
            var title = $"{Subjects[n % Subjects.Length]} {Problems[n / Subjects.Length % Problems.Length]} {Contexts[n / 80 % Contexts.Length]} ref{n}";
            table.Rows.Add(
                id, appId, nameof(AppEnvironment.Production), title, "<p>Seeded.</p>", nameof(Severity.Medium),
                nameof(IncidentStatus.New), nameof(IncidentSource.Manual), reporterId, id.ToString("N"), false, now.AddSeconds(-n), now);
        }

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var copy = new SqlBulkCopy(connection) { DestinationTableName = "dbo.Incidents", BatchSize = 5000, BulkCopyTimeout = 300 };
        foreach (DataColumn column in table.Columns)
        {
            copy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await copy.WriteToServerAsync(table);
    }

    /// <summary>Waits until the full-text catalog is idle and has indexed every incident row.</summary>
    public async Task WaitForFullTextPopulationAsync(TimeSpan timeout)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);
        await EventuallyAsync(
            async () =>
            {
                var rows = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM dbo.Incidents").SingleAsync();
                var indexed = await db.Database
                    .SqlQueryRaw<int>("SELECT CONVERT(int, FULLTEXTCATALOGPROPERTY('ftIncidentHub', 'ItemCount')) AS Value")
                    .SingleAsync();
                var idle = await db.Database
                    .SqlQueryRaw<int>("SELECT CONVERT(int, FULLTEXTCATALOGPROPERTY('ftIncidentHub', 'PopulateStatus')) AS Value")
                    .SingleAsync();
                return idle == 0 && indexed >= rows;
            },
            timeout);
    }

    /// <summary>Polls until <paramref name="condition"/> holds; fails with a timeout otherwise.</summary>
    public static async Task EventuallyAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(45));
        while (!await condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met before the timeout (full-text indexing?).");
            }

            await Task.Delay(500);
        }
    }
}
