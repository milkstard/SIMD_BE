using FluentAssertions;
using IncidentHub.Api.Hubs;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Hubs;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IncidentsHubTests(ApiFactory factory)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(500);

    private readonly IncidentSeeder _seeder = new(factory);

    [Fact]
    public async Task StartAsync_WithoutToken_IsRejected()
    {
        await using var connection = BuildConnection(headers: []);

        var start = () => connection.StartAsync();

        await start.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task StartAsync_UserWithNoMappedRole_IsRejected()
    {
        await using var connection = BuildConnection(UserHeaders(Guid.NewGuid().ToString()));

        var start = () => connection.StartAsync();

        await start.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Connect_Member_IsInUserAndTeamGroups()
    {
        var appId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var teamId = await TeamIdOfAsync(appId);

        await using var client = await ConnectAsync(oid, UserRole.Reporter);

        await AssertInGroupAsync(client, HubGroups.User(client.UserId));
        await AssertInGroupAsync(client, HubGroups.Team(teamId));
    }

    [Fact]
    public async Task Connect_Reporter_NotInRespondersGroup()
    {
        var appId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var teamId = await TeamIdOfAsync(appId);

        await using var client = await ConnectAsync(oid, UserRole.Reporter);

        await AssertNotInGroupAsync(client, HubGroups.Responders(teamId));
    }

    [Theory]
    [InlineData(UserRole.Responder)]
    [InlineData(UserRole.TeamLead)]
    [InlineData(UserRole.Admin)]
    public async Task Connect_ResponderLevelRole_InRespondersGroup(UserRole role)
    {
        var appId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var teamId = await TeamIdOfAsync(appId);

        await using var client = await ConnectAsync(oid, role);

        await AssertInGroupAsync(client, HubGroups.Responders(teamId));
    }

    [Fact]
    public async Task Connect_NonMember_NotInOtherTeamGroups()
    {
        var otherTeamId = await TeamIdOfAsync(await factory.CreateAppAsync());

        await using var client = await ConnectAsync(Guid.NewGuid().ToString(), UserRole.Responder);

        await AssertNotInGroupAsync(client, HubGroups.Team(otherTeamId));
        await AssertNotInGroupAsync(client, HubGroups.Responders(otherTeamId));
    }

    [Fact]
    public async Task WatchIncident_Member_JoinsIncidentGroup()
    {
        var appId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var (incidentId, _) = await _seeder.AddIncidentAsync(appId, "Watch me");
        await using var client = await ConnectAsync(oid, UserRole.Reporter);

        await client.Connection.InvokeAsync("WatchIncident", incidentId);

        await AssertInGroupAsync(client, HubGroups.Incident(incidentId));
    }

    [Fact]
    public async Task WatchIncident_NonMember_ThrowsHubException()
    {
        var appId = await factory.CreateAppAsync();
        var (incidentId, _) = await _seeder.AddIncidentAsync(appId, "Not yours");
        await using var client = await ConnectAsync(Guid.NewGuid().ToString(), UserRole.Responder);

        var watch = () => client.Connection.InvokeAsync("WatchIncident", incidentId);

        (await watch.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain(IncidentsHub.IncidentNotFound);
        await AssertNotInGroupAsync(client, HubGroups.Incident(incidentId));
    }

    [Fact]
    public async Task WatchIncident_UnknownId_ThrowsSameMessage()
    {
        await using var client = await ConnectAsync(Guid.NewGuid().ToString(), UserRole.Responder);

        var watch = () => client.Connection.InvokeAsync("WatchIncident", Guid.NewGuid());

        (await watch.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain(IncidentsHub.IncidentNotFound);
    }

    [Fact]
    public async Task UnwatchIncident_AfterWatch_LeavesGroup()
    {
        var appId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var (incidentId, _) = await _seeder.AddIncidentAsync(appId, "Watch then leave");
        await using var client = await ConnectAsync(oid, UserRole.Responder);
        await client.Connection.InvokeAsync("WatchIncident", incidentId);

        await client.Connection.InvokeAsync("UnwatchIncident", incidentId);

        await AssertNotInGroupAsync(client, HubGroups.Incident(incidentId));
    }

    [Fact]
    public async Task JoinDashboard_JoinsOnlyOwnTeams()
    {
        var ownApp = await factory.CreateAppAsync();
        var secondApp = await factory.CreateAppAsync();
        var otherTeamId = await TeamIdOfAsync(await factory.CreateAppAsync());
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, ownApp);
        await factory.AddToTeamAsync(oid, secondApp);
        await using var client = await ConnectAsync(oid, UserRole.Responder);

        await client.Connection.InvokeAsync("JoinDashboard");

        await AssertInGroupAsync(client, HubGroups.Dashboard(await TeamIdOfAsync(ownApp)));
        await AssertInGroupAsync(client, HubGroups.Dashboard(await TeamIdOfAsync(secondApp)));
        await AssertNotInGroupAsync(client, HubGroups.Dashboard(otherTeamId));
    }

    [Fact]
    public async Task JoinDashboard_UserInNoTeam_JoinsNothing()
    {
        var teamId = await TeamIdOfAsync(await factory.CreateAppAsync());
        await using var client = await ConnectAsync(Guid.NewGuid().ToString(), UserRole.Responder);

        await client.Connection.InvokeAsync("JoinDashboard");

        await AssertNotInGroupAsync(client, HubGroups.Dashboard(teamId));
    }

    [Fact]
    public async Task LeaveDashboard_AfterJoin_LeavesGroups()
    {
        var appId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var teamId = await TeamIdOfAsync(appId);
        await using var client = await ConnectAsync(oid, UserRole.Responder);
        await client.Connection.InvokeAsync("JoinDashboard");

        await client.Connection.InvokeAsync("LeaveDashboard");

        await AssertNotInGroupAsync(client, HubGroups.Dashboard(teamId));
    }

    private async Task<Guid> TeamIdOfAsync(Guid applicationId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Applications.Where(a => a.Id == applicationId).Select(a => a.OwningTeamId).SingleAsync();
    }

    private Dictionary<string, string> UserHeaders(string oid, params UserRole[] roles) => new()
    {
        [TestAuthHandler.OidHeader] = oid,
        [TestAuthHandler.GroupsHeader] = string.Join(',', roles.Select(factory.GroupFor)),
    };

    private HubConnection BuildConnection(Dictionary<string, string> headers) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/incidents"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                foreach (var (name, value) in headers)
                {
                    options.Headers[name] = value;
                }
            })
            .Build();

    /// <summary>Starts a connection and waits until its group membership is live (OnConnectedAsync runs after the handshake).</summary>
    private async Task<HubClient> ConnectAsync(string oid, UserRole role)
    {
        var userId = await factory.EnsureUserAsync(oid);
        var client = new HubClient(BuildConnection(UserHeaders(oid, role)), userId);
        await client.Connection.StartAsync();
        await AssertInGroupAsync(client, HubGroups.User(userId));
        return client;
    }

    private static async Task AssertInGroupAsync(HubClient client, string group, ApiFactory factory)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!await SendAndWaitAsync(factory, client, group, Poll))
        {
            cts.IsCancellationRequested.Should().BeFalse($"the connection should be in group {group}");
        }
    }

    private Task AssertInGroupAsync(HubClient client, string group) => AssertInGroupAsync(client, group, factory);

    private async Task AssertNotInGroupAsync(HubClient client, string group) =>
        (await SendAndWaitAsync(factory, client, group, QuietPeriod)).Should().BeFalse($"the connection should not be in group {group}");

    private static async Task<bool> SendAndWaitAsync(ApiFactory factory, HubClient client, string group, TimeSpan wait)
    {
        var marker = Guid.NewGuid();
        var response = await factory.CreateClient().PostAsync($"/probe/hub/groups/{group}?marker={marker}", content: null);
        response.EnsureSuccessStatusCode();

        using var cts = new CancellationTokenSource(wait);
        while (!cts.IsCancellationRequested)
        {
            if (client.Has(marker))
            {
                return true;
            }

            await Task.Delay(25, CancellationToken.None);
        }

        return client.Has(marker);
    }

    private sealed class HubClient : IAsyncDisposable
    {
        private readonly List<Guid> _markers = [];

        public HubClient(HubConnection connection, Guid userId)
        {
            Connection = connection;
            UserId = userId;
            connection.On<CommentAddedEvent>("CommentAdded", e => { lock (_markers) { _markers.Add(e.IncidentId); } });
        }

        public HubConnection Connection { get; }

        public Guid UserId { get; }

        public bool Has(Guid marker)
        {
            lock (_markers)
            {
                return _markers.Contains(marker);
            }
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }
}
