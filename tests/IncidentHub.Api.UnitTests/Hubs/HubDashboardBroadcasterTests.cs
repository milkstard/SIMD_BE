using IncidentHub.Api.Hubs;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Dashboard.Dtos;
using IncidentHub.Application.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace IncidentHub.Api.UnitTests.Hubs;

public sealed class HubDashboardBroadcasterTests
{
    private readonly Guid _teamId = Guid.NewGuid();
    private readonly IIncidentClient _client = Substitute.For<IIncidentClient>();
    private readonly IHubContext<IncidentsHub, IIncidentClient> _hub = Substitute.For<IHubContext<IncidentsHub, IIncidentClient>>();

    private HubDashboardBroadcaster Create(IDashboardSummaryReader reader)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => reader);
        _hub.Clients.Group(HubGroups.Dashboard(_teamId)).Returns(_client);
        return new HubDashboardBroadcaster(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _hub);
    }

    [Fact]
    public async Task Send_NullReader_SendsNothing()
    {
        var sut = Create(new NullDashboardSummaryReader());

        await sut.SendAsync(_teamId, default);

        await _client.DidNotReceiveWithAnyArgs().DashboardChanged(default!);
    }

    [Fact]
    public async Task Send_ReaderReturnsSummary_SendsItToTheTeamDashboard()
    {
        var summary = new DashboardSummaryDto("7d", DateTimeOffset.UnixEpoch, 0, 0, 0, new Dictionary<IncidentHub.Domain.Incidents.Severity, int>(), [], null, null, []);
        var reader = Substitute.For<IDashboardSummaryReader>();
        reader.GetAsync(_teamId, Arg.Any<CancellationToken>()).Returns(summary);
        var sut = Create(reader);

        await sut.SendAsync(_teamId, default);

        await _client.Received(1).DashboardChanged(summary);
    }
}
