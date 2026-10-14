using System.Diagnostics;
using System.Net;
using System.Xml.Linq;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Users;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace IncidentHub.Api.IntegrationTests.Performance;

/// <summary>
/// Opt-in: <c>dotnet test --filter Category=Performance</c>. Seeds 50k incidents and reads the cached plan of the open-incident
/// grouping behind <c>GET /dashboard/summary</c>: it must not scan the clustered index. The other queries are only reported.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Performance")]
public sealed class DashboardSummaryQueryPlanTests(ApiFactory factory, ITestOutputHelper output)
{
    private const int TargetRows = 10_000;
    private const int OtherRows = 40_000;
    private const int Calls = 50;

    private string ConnectionString => factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Sql"]!;

    [Fact]
    public async Task Summary_50kSeededRows_OpenCountIsNotAClusteredScan()
    {
        var seeder = new IncidentSeeder(factory);
        var targetApp = await factory.CreateAppAsync();
        var otherApps = new[] { await factory.CreateAppAsync(), await factory.CreateAppAsync(), await factory.CreateAppAsync(), await factory.CreateAppAsync() };
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, targetApp);
        var reporterId = await factory.EnsureUserAsync(oid);

        await seeder.BulkInsertAsync(targetApp, reporterId, TargetRows);
        foreach (var (app, index) in otherApps.Select((a, i) => (a, i)))
        {
            await seeder.BulkInsertAsync(app, reporterId, OtherRows / otherApps.Length, firstIndex: TargetRows + (index * OtherRows));
        }

        // Most real incidents are closed: leave a small open slice so the open count is selective.
        await ExecuteAsync("UPDATE dbo.Incidents SET Status = 'Closed', ClosedAt = SYSDATETIMEOFFSET(), CloseReason = 'Fixed' WHERE Status = 'New'");
        await ExecuteAsync(
            $"""
            UPDATE TOP (150) dbo.Incidents SET Status = 'Triaged', ClosedAt = NULL, CloseReason = NULL
            WHERE ApplicationId = '{targetApp}'
            """);
        await ExecuteAsync("UPDATE STATISTICS dbo.Incidents WITH FULLSCAN");

        var client = factory.CreateClient().AsUser(factory, oid, UserRole.Responder);
        await ExecuteAsync("DBCC FREEPROCCACHE");
        (await client.GetAsync("/api/v1/dashboard/summary?range=30d")).StatusCode.Should().Be(HttpStatusCode.OK);

        var open = await PlanAsync("%GROUP BY%[[]Severity]%");
        output.WriteLine($"open grouping: {Describe(open)}");
        output.WriteLine($"summary p95  : {await P95Async(client):F1} ms");

        ScansClusteredIndex(open).Should().BeFalse("the open count must be index-backed");
    }

    private async Task<string> PlanAsync(string like)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            SELECT TOP (1) CAST(qp.query_plan AS nvarchar(max))
            FROM sys.dm_exec_cached_plans cp
            CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
            CROSS APPLY sys.dm_exec_query_plan(cp.plan_handle) qp
            WHERE st.text LIKE '%[[]Incidents] AS [[]i]%' AND st.text LIKE @like AND st.text NOT LIKE '%dm_exec%'
            ORDER BY cp.usecounts DESC
            """,
            connection);
        command.Parameters.AddWithValue("@like", like);
        var plan = (string?)await command.ExecuteScalarAsync();
        plan.Should().NotBeNull("the open grouping plan must be in the plan cache");
        return plan!;
    }

    private async Task<double> P95Async(HttpClient client)
    {
        var durations = new List<double>();
        for (var i = 0; i < Calls; i++)
        {
            // Bypass the 5 s cache: every call must run the queries.
            await factory.Services.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>().GetDatabase()
                .ExecuteAsync("FLUSHDB");
            var stopwatch = Stopwatch.StartNew();
            (await client.GetAsync("/api/v1/dashboard/summary?range=30d")).StatusCode.Should().Be(HttpStatusCode.OK);
            durations.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        durations.Sort();
        return durations[(int)Math.Ceiling(Calls * 0.95) - 1];
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 300 };
        await command.ExecuteNonQueryAsync();
    }

    private static bool ScansClusteredIndex(string plan) =>
        IncidentAccesses(plan).Any(a => a.Operator == "Clustered Index Scan");

    private static string Describe(string plan) =>
        string.Join(", ", IncidentAccesses(plan).Select(a => $"{a.Operator} on {a.Index}").Distinct());

    private static IEnumerable<(string Operator, string Index)> IncidentAccesses(string plan)
    {
        var document = XDocument.Parse(plan);
        foreach (var op in document.Descendants().Where(e => e.Name.LocalName == "RelOp"))
        {
            var physical = (string?)op.Attribute("PhysicalOp") ?? string.Empty;
            foreach (var target in op.Elements().Where(e => e.Name.LocalName is "IndexScan" or "TableScan").Elements()
                         .Where(e => e.Name.LocalName == "Object" && (string?)e.Attribute("Table") == "[Incidents]"))
            {
                yield return (physical, ((string?)target.Attribute("Index") ?? string.Empty).Trim('[', ']'));
            }
        }
    }
}
