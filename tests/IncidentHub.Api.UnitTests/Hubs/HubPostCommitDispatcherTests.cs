using FluentAssertions;
using IncidentHub.Api.Hubs;
using IncidentHub.Application.Common;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Application.Realtime;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace IncidentHub.Api.UnitTests.Hubs;

public sealed class HubPostCommitDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly Guid _incidentId = Guid.NewGuid();
    private readonly Guid _teamId = Guid.NewGuid();
    private readonly IIncidentClient _client = Substitute.For<IIncidentClient>();
    private readonly RecordingDashboardBroadcaster _dashboard = new();
    private readonly HubPostCommitDispatcher _sut;

    public HubPostCommitDispatcherTests()
    {
        var reader = Substitute.For<IIncidentBroadcastReader>();
        var summary = new IncidentSummaryDto(
            _incidentId, "INC-1001", "Title", new ApplicationRefDto(Guid.NewGuid(), "App", "APP"), AppEnvironment.Production,
            Severity.High, null, IncidentStatus.New, null, null, new UserRefDto(Guid.NewGuid(), "R", "r@test.local"),
            Now, Now, null, null, false, 0, "AAAA");
        reader.GetIncidentAsync(_incidentId, Arg.Any<CancellationToken>()).Returns(new BroadcastIncident(summary, _teamId));
        var comment = new CommentDto(
            Guid.NewGuid(), _incidentId, new UserRefDto(Guid.NewGuid(), "A", "a@test.local"), "<p>hi</p>", false, [], Now, null);
        reader.GetCommentAsync(comment.Id, Arg.Any<CancellationToken>()).Returns(comment);
        _commentId = comment.Id;

        var services = new ServiceCollection();
        services.AddScoped(_ => reader);
        services.AddScoped<IncidentBroadcaster>();

        var hub = Substitute.For<IHubContext<IncidentsHub, IIncidentClient>>();
        hub.Clients.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(_client);

        var throttle = new DashboardThrottle(new FakeTimeProvider(), _dashboard, NullLogger<DashboardThrottle>.Instance);
        _sut = new HubPostCommitDispatcher(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), hub, throttle);
    }

    private readonly Guid _commentId;

    private IDomainEvent[] ReportedThenCommented() =>
    [
        new IncidentReported(_incidentId, Guid.NewGuid(), AppEnvironment.Production, Severity.High, IncidentSource.Manual, Guid.NewGuid(), Now),
        new CommentAdded(_incidentId, _commentId, false, null, Now),
    ];

    [Fact]
    public async Task Dispatch_AllSendsSucceed_SendsEachMessageAndNotifiesDashboard()
    {
        await _sut.DispatchAsync(ReportedThenCommented(), default);

        await _client.Received(1).IncidentCreated(Arg.Any<IncidentSummaryDto>());
        await _client.Received(1).CommentAdded(Arg.Any<CommentAddedEvent>());
        _dashboard.Teams.Should().Equal(_teamId);
    }

    [Fact]
    public async Task Dispatch_FirstSendThrows_StillSendsTheRestNotifiesDashboardAndFails()
    {
        _client.IncidentCreated(Arg.Any<IncidentSummaryDto>()).Returns(Task.FromException(new InvalidOperationException("backplane down")));

        var dispatch = () => _sut.DispatchAsync(ReportedThenCommented(), default);

        var failure = await dispatch.Should().ThrowAsync<AggregateException>();
        failure.Which.InnerExceptions.Should().ContainSingle().Which.Message.Should().Be("backplane down");
        await _client.Received(1).CommentAdded(Arg.Any<CommentAddedEvent>());
        _dashboard.Teams.Should().Equal(_teamId);
    }

    private sealed class RecordingDashboardBroadcaster : IDashboardBroadcaster
    {
        public List<Guid> Teams { get; } = [];

        public Task SendAsync(Guid teamId, CancellationToken cancellationToken)
        {
            Teams.Add(teamId);
            return Task.CompletedTask;
        }
    }
}
