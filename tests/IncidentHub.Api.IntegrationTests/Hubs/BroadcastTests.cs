using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using static IncidentHub.Api.IntegrationTests.Hubs.BroadcastClient;
using static IncidentHub.Api.IntegrationTests.Incidents.IncidentWriteHelpers;

namespace IncidentHub.Api.IntegrationTests.Hubs;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class BroadcastTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    private static string NewOid() => Guid.NewGuid().ToString();

    private sealed record Scenario(HttpClient Client, string Oid, Guid AppId, Guid Id, string Number, Guid CallerId);

    private async Task<Scenario> SeedAsync()
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        var callerId = await factory.EnsureUserAsync(oid);
        var (id, number) = await _seeder.AddIncidentAsync(appId, $"Checkout fails {Guid.NewGuid():N}");
        return new Scenario(factory.CreateClient().AsUser(factory, oid, UserRole.Responder), oid, appId, id, number, callerId);
    }

    private async Task<BroadcastClient> WatcherAsync(Scenario s)
    {
        var client = await ConnectAsync(factory, s.Oid, UserRole.Responder);
        await client.WatchAsync(s.Id);
        return client;
    }

    private async Task<BroadcastClient> TeammateAsync(Scenario s, UserRole role, bool watch)
    {
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, s.AppId);
        var hub = await ConnectAsync(factory, oid, role);
        if (watch)
        {
            await hub.WatchAsync(s.Id);
        }

        return hub;
    }

    private static object NewIncident(Guid appId, string title) =>
        new { applicationId = appId, environment = "Production", severity = "High", title, description = "<p>Users see an error page.</p>" };

    [Fact]
    public async Task Patch_Committed_WatcherReceivesIncidentUpdated()
    {
        var s = await SeedAsync();
        await using var watcher = await WatcherAsync(s);
        var title = $"Renamed {Guid.NewGuid():N}";

        var response = await PatchAsync(s.Client, s.Id, new { title }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var received = await WaitForAsync(watcher.Updated, u => u.Id == s.Id);
        received.Title.Should().Be(title);
        received.RowVersion.Should().Be((await JsonAsync(response)).GetProperty("rowVersion").GetString());
    }

    [Fact]
    public async Task Transition_Committed_WatcherReceivesIncidentUpdated()
    {
        var s = await SeedAsync();
        await using var watcher = await WatcherAsync(s);

        var response = await PostAsync(s.Client, s.Id, "transitions", new { to = "Triaged" }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var received = await WaitForAsync(watcher.Updated, u => u.Id == s.Id);
        received.Status.Should().Be(IncidentStatus.Triaged);
    }

    [Fact]
    public async Task Assign_Committed_WatcherReceivesIncidentUpdated()
    {
        var s = await SeedAsync();
        await using var watcher = await WatcherAsync(s);

        var response = await PostAsync(s.Client, s.Id, "assignment", new { assigneeId = s.CallerId, teamId = (Guid?)null }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var received = await WaitForAsync(watcher.Updated, u => u.Id == s.Id);
        received.Assignee!.Id.Should().Be(s.CallerId);
    }

    [Fact]
    public async Task Report_Committed_TeamClientReceivesIncidentCreated()
    {
        var s = await SeedAsync();
        await using var outsider = await ConnectAsync(factory, NewOid(), UserRole.Reporter);
        await using var member = await ConnectAsync(factory, s.Oid, UserRole.Responder);
        var title = $"Checkout breaks {Guid.NewGuid():N}";

        var response = await s.Client.PostAsJsonAsync("/api/v1/incidents", NewIncident(s.AppId, title));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await WaitForAsync(member.Created, c => c.Title == title);
        created.Number.Should().StartWith("INC-");
        (await ReceivesNoneAsync(outsider.Created, c => c.Title == title)).Should().BeTrue();
    }

    [Fact]
    public async Task Report_OtherTeamClient_ReceivesNothing()
    {
        var s = await SeedAsync();
        var otherApp = await factory.CreateAppAsync();
        var otherOid = NewOid();
        await factory.AddToTeamAsync(otherOid, otherApp);
        await using var other = await ConnectAsync(factory, otherOid, UserRole.Responder);
        var title = $"Checkout breaks {Guid.NewGuid():N}";

        (await s.Client.PostAsJsonAsync("/api/v1/incidents", NewIncident(s.AppId, title))).StatusCode.Should().Be(HttpStatusCode.Created);

        (await ReceivesNoneAsync(other.Created, c => c.Title == title)).Should().BeTrue();
    }

    [Fact]
    public async Task Save_Fails_NothingBroadcast()
    {
        var s = await SeedAsync();
        await using var watcher = await WatcherAsync(s);
        var title = $"Checkout breaks {Guid.NewGuid():N}";
        (await s.Client.PostAsJsonAsync("/api/v1/incidents", NewIncident(s.AppId, title))).StatusCode.Should().Be(HttpStatusCode.Created);
        await WaitForAsync(watcher.Created, c => c.Title == title);
        var createdBefore = watcher.Created.Count;

        var duplicate = await s.Client.PostAsJsonAsync("/api/v1/incidents", NewIncident(s.AppId, title));
        var stale = await PatchAsync(s.Client, s.Id, new { title = "Stale edit" }, "\"AAAAAAAAB9E=\"");

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReceivesNoneAsync(watcher.Updated, u => u.Id == s.Id)).Should().BeTrue();
        watcher.Created.Count.Should().Be(createdBefore);
    }

    [Fact]
    public async Task Broadcast_HubThrows_RequestStillSucceeds()
    {
        var s = await SeedAsync();
        var logger = new FakeLogger<PostCommitHook>();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPostCommitDispatcher>();
            services.AddSingleton<IPostCommitDispatcher, ThrowingDispatcher>();
            services.AddSingleton<ILogger<PostCommitHook>>(logger);
        }));
        var client = host.CreateClient().AsUser(factory, s.Oid, UserRole.Responder);
        var title = $"Renamed {Guid.NewGuid():N}";

        var response = await PatchAsync(client, s.Id, new { title }, await ETagAsync(client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("title").GetString().Should().Be(title);
        logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Level == LogLevel.Warning && r.Message.StartsWith("Broadcast of IncidentUpdated for incident"));
    }

    [Fact]
    public async Task InternalComment_ReporterClient_ReceivesNothing()
    {
        var s = await SeedAsync();
        await using var reporter = await TeammateAsync(s, UserRole.Reporter, watch: true);
        await using var responder = await TeammateAsync(s, UserRole.Responder, watch: false);
        var body = $"<p>Root cause {Guid.NewGuid():N}</p>";

        var response = await s.Client.PostAsJsonAsync($"/api/v1/incidents/{s.Id}/comments", new { body, isInternal = true });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var received = await WaitForAsync(responder.Comments, c => c.IncidentId == s.Id);
        received.Comment.IsInternal.Should().BeTrue();
        (await ReceivesNoneAsync(reporter.Comments, c => c.IncidentId == s.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task PublicComment_WatcherReceivesCommentAdded()
    {
        var s = await SeedAsync();
        await using var reporter = await TeammateAsync(s, UserRole.Reporter, watch: true);
        var body = $"<p>Any news {Guid.NewGuid():N}</p>";

        var response = await s.Client.PostAsJsonAsync($"/api/v1/incidents/{s.Id}/comments", new { body, isInternal = false });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var received = await WaitForAsync(reporter.Comments, c => c.IncidentId == s.Id);
        received.Comment.Body.Should().Be(body);
        received.Comment.IsInternal.Should().BeFalse();
    }

    [Fact]
    public async Task TwoHosts_SharedRedis_ClientOnOtherHostReceivesEvent()
    {
        var s = await SeedAsync();
        using var hostB = factory.WithWebHostBuilder(_ => { });
        await using var watcher = await ConnectAsync(factory, s.Oid, UserRole.Responder, hostB);
        await watcher.WatchAsync(s.Id);

        // The change goes through host A (the factory host itself); the client is connected to host B.
        var response = await PostAsync(s.Client, s.Id, "transitions", new { to = "Triaged" }, await ETagAsync(s.Client, s.Number));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await WaitForAsync(watcher.Updated, u => u.Id == s.Id)).Number.Should().Be(s.Number);
    }

    private sealed class ThrowingDispatcher : IPostCommitDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("hub down");
    }
}
