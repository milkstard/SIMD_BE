using System.Text.Json.Serialization;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Users;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Hubs;

/// <summary>A real hub connection that records every broadcast it receives, for the post-commit tests.</summary>
internal sealed class BroadcastClient : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(750);

    private readonly HubConnection _connection;

    private BroadcastClient(HubConnection connection)
    {
        _connection = connection;
        connection.On<IncidentSummaryDto>("IncidentCreated", e => Add(Created, e));
        connection.On<IncidentSummaryDto>("IncidentUpdated", e => Add(Updated, e));
        connection.On<CommentAddedEvent>("CommentAdded", e => Add(Comments, e));
    }

    public List<IncidentSummaryDto> Created { get; } = [];

    public List<IncidentSummaryDto> Updated { get; } = [];

    public List<CommentAddedEvent> Comments { get; } = [];

    /// <summary>Connects as the user and joins the dashboards of their teams; the call returns once the connection groups are live.</summary>
    public static async Task<BroadcastClient> ConnectAsync(
        ApiFactory factory, string oid, UserRole role, WebApplicationFactory<Program>? host = null)
    {
        host ??= factory;
        await factory.EnsureUserAsync(oid);
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(host.Server.BaseAddress, "/hubs/incidents"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
                options.Headers[TestAuthHandler.OidHeader] = oid;
                options.Headers[TestAuthHandler.GroupsHeader] = factory.GroupFor(role);
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        var client = new BroadcastClient(connection);
        await connection.StartAsync();

        // Hub methods run after OnConnectedAsync, so once this returns the user, team and responders groups are joined.
        await connection.InvokeAsync("JoinDashboard");
        return client;
    }

    public Task WatchAsync(Guid incidentId) => _connection.InvokeAsync("WatchIncident", incidentId);

    public static async Task<T> WaitForAsync<T>(List<T> received, Func<T, bool> match)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!cts.IsCancellationRequested)
        {
            if (Find(received, match) is { } found)
            {
                return found;
            }

            await Task.Delay(25, CancellationToken.None);
        }

        throw new TimeoutException($"No matching {typeof(T).Name} arrived within {Timeout}.");
    }

    public static async Task<bool> ReceivesNoneAsync<T>(List<T> received, Func<T, bool> match)
    {
        await Task.Delay(Quiet);
        return Find(received, match) is null;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private static T? Find<T>(List<T> received, Func<T, bool> match)
    {
        lock (received)
        {
            return received.FirstOrDefault(match);
        }
    }

    private static void Add<T>(List<T> received, T item)
    {
        lock (received)
        {
            received.Add(item);
        }
    }
}
