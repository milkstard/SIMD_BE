using System.Diagnostics;
using System.Net;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Users;
using Xunit.Abstractions;

namespace IncidentHub.Api.IntegrationTests.Performance;

/// <summary>Opt-in: <c>dotnet test --filter Category=Performance</c>. Exclude from the default run with <c>--filter Category!=Performance</c>.</summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Performance")]
public sealed class SimilarIncidentsPerformanceTests(ApiFactory factory, ITestOutputHelper output)
{
    private const int TargetRows = 10_000;
    private const int OtherRows = 40_000;
    private const int WarmUps = 10;
    private const int Calls = 100;

    private static readonly string[] Queries =
    [
        "checkout fails after deploy", "payment times out", "login returns errors", "search is slow", "invoice crashes under load",
        "report shows blank page", "upload loses data", "profile rejects requests", "catalog fails on mobile", "inventory is slow at midnight",
    ];

    [Fact]
    public async Task Similar_50kSeededRows_P95Under300Ms()
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

        await seeder.WaitForFullTextPopulationAsync(TimeSpan.FromMinutes(5));

        var client = factory.CreateClient().AsUser(factory, oid, UserRole.Responder);
        for (var i = 0; i < WarmUps; i++)
        {
            await GetAsync(client, targetApp, Queries[i % Queries.Length]);
        }

        var durations = new List<double>();
        for (var i = 0; i < Calls; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            await GetAsync(client, targetApp, Queries[i % Queries.Length]);
            durations.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        durations.Sort();
        var p95 = durations[(int)Math.Ceiling(Calls * 0.95) - 1];
        output.WriteLine($"p50={durations[Calls / 2]:F1} ms  p95={p95:F1} ms  max={durations[^1]:F1} ms");

        p95.Should().BeLessThan(300);
    }

    private static async Task GetAsync(HttpClient client, Guid appId, string title)
    {
        var response = await client.GetAsync($"/api/v1/incidents/similar?applicationId={appId}&title={Uri.EscapeDataString(title)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
