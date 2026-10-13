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
/// Opt-in: <c>dotnet test --filter Category=Performance</c>. Seeds 50k incidents and reads the cached execution plan of the list query:
/// selective filters must use the two list indexes and never scan the clustered index; the default list is only reported.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Performance")]
public sealed class ListIncidentsQueryPlanTests(ApiFactory factory, ITestOutputHelper output)
{
    private const int TargetRows = 10_000;
    private const int OtherRows = 40_000;
    private const int Calls = 50;

    private const string StatusSeverityIndex = "IX_Incidents_Status_Severity";
    private const string ApplicationCreatedIndex = "IX_Incidents_Application_CreatedAt";

    private string ConnectionString => factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Sql"]!;

    [Fact]
    public async Task List_50kSeededRows_SelectiveFiltersUseListIndexes()
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

        // Make Status/Severity selective: the seed is all New/Medium, so promote a small slice of the target application.
        await ExecuteAsync(
            $"""
            UPDATE TOP (150) dbo.Incidents SET Status = 'Triaged', Severity = 'Critical'
            WHERE ApplicationId = '{targetApp}'
            """);
        await ExecuteAsync("UPDATE STATISTICS dbo.Incidents WITH FULLSCAN");

        var client = factory.CreateClient().AsUser(factory, oid, UserRole.Responder);
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-10).ToString("o"));

        var statusSeverity = await PlanOfAsync(client, "status=Triaged&severity=Critical&limit=25");
        var applicationAndDates = await PlanOfAsync(client, $"applicationId={targetApp}&createdFrom={from}&limit=25");
        var defaultList = await PlanOfAsync(client, "limit=25");

        output.WriteLine($"status+severity : {Describe(statusSeverity)}");
        output.WriteLine($"app+date range  : {Describe(applicationAndDates)}");
        output.WriteLine($"default list    : {Describe(defaultList)}");
        output.WriteLine($"default list p95: {await P95Async(client):F1} ms");

        UsesListIndex(statusSeverity).Should().BeTrue("status+severity is selective");
        ScansClusteredIndex(statusSeverity).Should().BeFalse();
        UsesListIndex(applicationAndDates).Should().BeTrue("application + date range is selective");
        ScansClusteredIndex(applicationAndDates).Should().BeFalse();
    }

    private async Task<string> PlanOfAsync(HttpClient client, string query)
    {
        await ExecuteAsync("DBCC FREEPROCCACHE");

        var response = await client.GetAsync($"/api/v1/incidents?{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            SELECT TOP (1) CAST(qp.query_plan AS nvarchar(max))
            FROM sys.dm_exec_cached_plans cp
            CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
            CROSS APPLY sys.dm_exec_query_plan(cp.plan_handle) qp
            WHERE st.text LIKE '%[[]Incidents] AS [[]i]%' AND st.text LIKE '%ORDER BY%' AND st.text NOT LIKE '%[[]Users]%' AND st.text NOT LIKE '%dm_exec%'
            ORDER BY cp.usecounts DESC
            """,
            connection);
        var plan = (string?)await command.ExecuteScalarAsync();
        plan.Should().NotBeNull("the page query plan must be in the plan cache");
        return plan!;
    }

    private async Task<double> P95Async(HttpClient client)
    {
        var durations = new List<double>();
        for (var i = 0; i < Calls; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            (await client.GetAsync("/api/v1/incidents?limit=25")).StatusCode.Should().Be(HttpStatusCode.OK);
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

    // Statistics names also contain the index names, so look at the Incidents objects the plan actually reads.
    private static bool UsesListIndex(string plan) =>
        IncidentAccesses(plan).Any(a => a.Index is StatusSeverityIndex or ApplicationCreatedIndex);

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
