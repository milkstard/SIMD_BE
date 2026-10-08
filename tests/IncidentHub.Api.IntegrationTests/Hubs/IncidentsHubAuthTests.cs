using System.Net.Http.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Users;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace IncidentHub.Api.IntegrationTests.Hubs;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IncidentsHubAuthTests(ApiFactory factory)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

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
    public async Task CommentAdded_Reporter_ReceivesPublicButNotInternalComments()
    {
        var appId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var received = new List<CommentDto>();

        await using var connection = BuildConnection(UserHeaders(oid, UserRole.Reporter));
        connection.On<CommentDto>("CommentAdded", comment => { lock (received) { received.Add(comment); } });
        await connection.StartAsync();
        await WaitUntilJoinedAsync(appId, received);

        await Broadcast(appId, "internal note", isInternal: true);
        await Broadcast(appId, "public update", isInternal: false);
        await WaitForBodyAsync(received, "public update");
        await Task.Delay(300); // let a wrongly routed internal comment arrive, if the routing were broken

        lock (received)
        {
            received.Select(c => c.Body).Should().Contain("public update").And.NotContain("internal note");
            received.Should().NotContain(c => c.IsInternal);
        }
    }

    [Fact]
    public async Task CommentAdded_Responder_ReceivesPublicAndInternalComments()
    {
        var appId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var received = new List<CommentDto>();

        await using var connection = BuildConnection(UserHeaders(oid, UserRole.Responder));
        connection.On<CommentDto>("CommentAdded", comment => { lock (received) { received.Add(comment); } });
        await connection.StartAsync();
        await WaitUntilJoinedAsync(appId, received);

        await Broadcast(appId, "internal note", isInternal: true);
        await Broadcast(appId, "public update", isInternal: false);
        await WaitForBodyAsync(received, "internal note");
        await WaitForBodyAsync(received, "public update");

        lock (received)
        {
            received.Select(c => c.Body).Should().Contain(["internal note", "public update"]);
        }
    }

    [Fact]
    public async Task CommentAdded_UserNotOnTeam_ReceivesNothing()
    {
        var appId = await factory.CreateAppAsync();
        var received = new List<CommentDto>();

        await using var connection = BuildConnection(UserHeaders(Guid.NewGuid().ToString(), UserRole.Responder));
        connection.On<CommentDto>("CommentAdded", comment => { lock (received) { received.Add(comment); } });
        await connection.StartAsync();

        await Broadcast(appId, "public update", isInternal: false);
        await Broadcast(appId, "internal note", isInternal: true);
        await Task.Delay(500);

        lock (received)
        {
            received.Should().BeEmpty();
        }
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

    private async Task Broadcast(Guid appId, string body, bool isInternal)
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync($"/probe/apps/{appId}/comments", new { body, isInternal });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>OnConnectedAsync runs after the handshake, so keep sending public comments until group membership is live.</summary>
    private async Task WaitUntilJoinedAsync(Guid appId, List<CommentDto> received)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!cts.IsCancellationRequested)
        {
            await Broadcast(appId, "sync", isInternal: false);
            await Task.Delay(100, CancellationToken.None);
            lock (received)
            {
                if (received.Any(c => c.Body == "sync"))
                {
                    received.Clear();
                    return;
                }
            }
        }

        throw new TimeoutException("Hub connection never joined the application group.");
    }

    private static async Task WaitForBodyAsync(List<CommentDto> received, string body)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!cts.IsCancellationRequested)
        {
            lock (received)
            {
                if (received.Any(c => c.Body == body))
                {
                    return;
                }
            }

            await Task.Delay(50, CancellationToken.None);
        }

        throw new TimeoutException($"Never received a comment with body '{body}'.");
    }
}
