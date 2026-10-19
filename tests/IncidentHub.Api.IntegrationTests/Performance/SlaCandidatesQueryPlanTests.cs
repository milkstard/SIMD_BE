using System.Diagnostics;
using System.Xml.Linq;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Api.IntegrationTests.Sla;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace IncidentHub.Api.IntegrationTests.Performance;

/// <summary>
/// Opt-in: <c>dotnet test --filter Category=Performance</c>. Seeds 50k incidents, most of them closed, and reads the cached plan of
/// the SLA monitor's candidate query: it must seek <c>IX_Incidents_SlaCandidates</c> and not scan the clustered index.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Performance")]
public sealed class SlaCandidatesQueryPlanTests(ApiFactory factory, ITestOutputHelper output)
{
    private const int Rows = 50_000;
    private const int OpenRows = 200;

    private string ConnectionString => factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Sql"]!;

    [Fact]
    public async Task Candidates_50kSeededRows_UsesTheSlaIndex()
    {
        var seeder = new IncidentSeeder(factory);
        var appId = await factory.CreateAppAsync();
        var reporterId = await factory.EnsureUserAsync(Guid.NewGuid().ToString());
        await seeder.BulkInsertAsync(appId, reporterId, Rows);

        // Most real incidents are closed; a small open slice has SLA times that are not due yet, so the pass changes nothing.
        await ExecuteAsync(
            $"""
            UPDATE dbo.Incidents SET Status = 'Closed', ClosedAt = SYSDATETIMEOFFSET(), CloseReason = 'Fixed'
            WHERE ApplicationId = '{appId}'
            """);
        await ExecuteAsync(
            $"""
            UPDATE TOP ({OpenRows}) dbo.Incidents
            SET Status = 'Triaged', ClosedAt = NULL, CloseReason = NULL,
                ResolveDueAt = DATEADD(day, 30, SYSDATETIMEOFFSET()), SlaWarnAt = DATEADD(day, 20, SYSDATETIMEOFFSET())
            WHERE ApplicationId = '{appId}'
            """);
        await ExecuteAsync("UPDATE STATISTICS dbo.Incidents WITH FULLSCAN");
        await ExecuteAsync("DBCC FREEPROCCACHE");

        var support = new SlaMonitorSupport(factory);
        var stopwatch = Stopwatch.StartNew();
        await support.RunHandlerAsync();
        output.WriteLine($"candidate pass: {stopwatch.Elapsed.TotalMilliseconds:F1} ms");

        var plan = await PlanAsync();
        output.WriteLine($"candidate query: {Describe(plan)}");

        ScansClusteredIndex(plan).Should().BeFalse("the candidate query must be index-backed");
        UsesIndex(plan, "IX_Incidents_SlaCandidates").Should().BeTrue("the filtered SLA index must serve the candidate query");
    }

    private async Task<string> PlanAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            SELECT TOP (1) CAST(qp.query_plan AS nvarchar(max))
            FROM sys.dm_exec_cached_plans cp
            CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
            CROSS APPLY sys.dm_exec_query_plan(cp.plan_handle) qp
            WHERE st.text LIKE '%[[]Incidents] AS [[]i]%' AND st.text LIKE '%[[]SlaWarnedAt] IS NULL%' AND st.text NOT LIKE '%dm_exec%'
            ORDER BY cp.usecounts DESC
            """,
            connection);
        var plan = (string?)await command.ExecuteScalarAsync();
        plan.Should().NotBeNull("the candidate query plan must be in the plan cache");
        return plan!;
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

    private static bool UsesIndex(string plan, string index) =>
        IncidentAccesses(plan).Any(a => a.Index == index);

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
