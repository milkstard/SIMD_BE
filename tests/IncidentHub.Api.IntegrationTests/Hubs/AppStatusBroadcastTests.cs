using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Users;
using static IncidentHub.Api.IntegrationTests.Hubs.BroadcastClient;
using static IncidentHub.Api.IntegrationTests.Incidents.IncidentWriteHelpers;

namespace IncidentHub.Api.IntegrationTests.Hubs;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AppStatusBroadcastTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    private static string NewOid() => Guid.NewGuid().ToString();

    private static object Report(Guid appId, string severity, string title) =>
        new { applicationId = appId, environment = "Production", severity, title, description = "<p>Users see an error page.</p>" };

    [Fact]
    public async Task Report_CriticalProduction_DashboardClientReceivesAppStatusChanged()
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        await using var dashboard = await ConnectAsync(factory, oid, UserRole.Responder);
        var client = factory.CreateClient().AsUser(factory, oid, UserRole.Responder);

        var response = await client.PostAsJsonAsync("/api/v1/incidents", Report(appId, "Critical", $"Checkout down {Guid.NewGuid():N}"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var received = await WaitForAsync(dashboard.AppStatuses, s => s.Application.Id == appId);
        received.Status.Should().Be(AppStatus.Outage);
    }

    [Fact]
    public async Task Update_StatusUnchanged_NoAppStatusChanged()
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        var (id, number) = await _seeder.AddIncidentAsync(appId, $"Checkout down {Guid.NewGuid():N}");
        var client = factory.CreateClient().AsUser(factory, oid, UserRole.Responder);
        await using var dashboard = await ConnectAsync(factory, oid, UserRole.Responder);

        // A title edit leaves the derived status (Degraded from the seeded High) untouched, so nothing is pushed.
        var response = await PatchAsync(client, id, new { title = $"Renamed {Guid.NewGuid():N}" }, await ETagAsync(client, number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReceivesNoneAsync(dashboard.AppStatuses, s => s.Application.Id == appId)).Should().BeTrue();
    }

    [Fact]
    public async Task Report_OtherTeamClient_ReceivesNothing()
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        var otherApp = await factory.CreateAppAsync();
        var otherOid = NewOid();
        await factory.AddToTeamAsync(otherOid, otherApp);
        await using var other = await ConnectAsync(factory, otherOid, UserRole.Responder);
        var client = factory.CreateClient().AsUser(factory, oid, UserRole.Responder);

        (await client.PostAsJsonAsync("/api/v1/incidents", Report(appId, "Critical", $"Checkout down {Guid.NewGuid():N}")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await ReceivesNoneAsync(other.AppStatuses, s => s.Application.Id == appId)).Should().BeTrue();
    }
}
