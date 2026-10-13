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
/// Opt-in: <c>dotnet test --filter Category=Performance</c>. Seeds comments on several incidents and reads the cached plan of the
/// comments page query: it must seek <c>IX_Comments_Incident_CreatedAt</c> and never scan the clustered index.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Performance")]
public sealed class CommentsQueryPlanTests(ApiFactory factory, ITestOutputHelper output)
{
    private const int CommentsPerIncident = 10_000;
    private const int Incidents = 5;
    private const string CommentsIndex = "IX_Comments_Incident_CreatedAt";

    private string ConnectionString => factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Sql"]!;

    [Fact]
    public async Task ListComments_ManySeededRows_SeeksTheIncidentCreatedAtIndex()
    {
        var seeder = new IncidentSeeder(factory);
        var appId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var authorId = await factory.EnsureUserAsync(oid);

        Guid target = Guid.Empty;
        for (var i = 0; i < Incidents; i++)
        {
            var (id, _) = await seeder.AddIncidentAsync(appId, $"Comment plan {Guid.NewGuid():N}");
            target = id;
            await ExecuteAsync(
                $"""
                INSERT INTO dbo.Comments (Id, IncidentId, AuthorId, Body, IsInternal, CreatedAt)
                SELECT NEWID(), '{id}', '{authorId}', N'<p>seeded</p>', 0, DATEADD(SECOND, n, SYSDATETIMEOFFSET())
                FROM (SELECT TOP ({CommentsPerIncident}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                      FROM sys.all_objects a CROSS JOIN sys.all_objects b) numbers
                """);
        }

        await ExecuteAsync("UPDATE STATISTICS dbo.Comments WITH FULLSCAN");
        await ExecuteAsync("DBCC FREEPROCCACHE");

        var client = factory.CreateClient().AsUser(factory, oid, UserRole.Responder);
        (await client.GetAsync($"/api/v1/incidents/{target}/comments?limit=25")).StatusCode.Should().Be(HttpStatusCode.OK);

        var plan = await PlanAsync();
        var accesses = CommentAccesses(plan).ToList();
        output.WriteLine(string.Join(", ", accesses.Select(a => $"{a.Operator} on {a.Index}").Distinct()));

        accesses.Should().Contain(a => a.Index == CommentsIndex);
        accesses.Should().NotContain(a => a.Operator == "Clustered Index Scan");
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
            WHERE st.text LIKE '%[[]Comments]%' AND st.text LIKE '%ORDER BY%' AND st.text NOT LIKE '%dm_exec%'
            ORDER BY cp.usecounts DESC
            """,
            connection);
        var plan = (string?)await command.ExecuteScalarAsync();
        plan.Should().NotBeNull("the comments page query plan must be in the plan cache");
        return plan!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 300 };
        await command.ExecuteNonQueryAsync();
    }

    private static IEnumerable<(string Operator, string Index)> CommentAccesses(string plan)
    {
        var document = XDocument.Parse(plan);
        foreach (var op in document.Descendants().Where(e => e.Name.LocalName == "RelOp"))
        {
            var physical = (string?)op.Attribute("PhysicalOp") ?? string.Empty;
            foreach (var target in op.Elements().Where(e => e.Name.LocalName is "IndexScan" or "TableScan").Elements()
                         .Where(e => e.Name.LocalName == "Object" && (string?)e.Attribute("Table") == "[Comments]"))
            {
                yield return (physical, ((string?)target.Attribute("Index") ?? string.Empty).Trim('[', ']'));
            }
        }
    }
}
