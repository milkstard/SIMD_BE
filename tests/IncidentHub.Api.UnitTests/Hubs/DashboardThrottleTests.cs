using FluentAssertions;
using IncidentHub.Api.Hubs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace IncidentHub.Api.UnitTests.Hubs;

public sealed class DashboardThrottleTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingBroadcaster _sent = new();
    private readonly Guid _team = Guid.NewGuid();

    private DashboardThrottle Sut => new(_time, _sent, NullLogger<DashboardThrottle>.Instance);

    [Fact]
    public void Notify_First_SendsImmediately()
    {
        var sut = Sut;

        sut.Notify(_team);

        _sent.Teams.Should().Equal(_team);
    }

    [Fact]
    public void Notify_BurstWithinWindow_SendsOneTrailing()
    {
        var sut = Sut;

        sut.Notify(_team);
        sut.Notify(_team);
        sut.Notify(_team);
        _sent.Teams.Should().HaveCount(1);

        _time.Advance(DashboardThrottle.Window);

        _sent.Teams.Should().HaveCount(2);
    }

    [Fact]
    public void Notify_BurstWithinWindow_NeverSendsTwiceInOneWindow()
    {
        var sut = Sut;

        sut.Notify(_team);
        sut.Notify(_team);
        _time.Advance(DashboardThrottle.Window);
        sut.Notify(_team);
        sut.Notify(_team);

        // The trailing send opened the next window: the later changes wait for it to end.
        _sent.Teams.Should().HaveCount(2);
        _time.Advance(DashboardThrottle.Window);
        _sent.Teams.Should().HaveCount(3);
        _time.Advance(DashboardThrottle.Window);
        _sent.Teams.Should().HaveCount(3);
    }

    [Fact]
    public void Notify_QuietGroup_SendsNothingMore()
    {
        var sut = Sut;
        sut.Notify(_team);

        _time.Advance(TimeSpan.FromSeconds(10));

        _sent.Teams.Should().HaveCount(1);
    }

    [Fact]
    public void Notify_AfterWindow_SendsAgain()
    {
        var sut = Sut;
        sut.Notify(_team);
        _time.Advance(DashboardThrottle.Window);

        sut.Notify(_team);

        _sent.Teams.Should().HaveCount(2);
    }

    [Fact]
    public void Notify_DifferentGroups_AreIndependent()
    {
        var sut = Sut;
        var other = Guid.NewGuid();

        sut.Notify(_team);
        sut.Notify(other);

        _sent.Teams.Should().BeEquivalentTo([_team, other]);
    }

    [Fact]
    public void Notify_SendThrows_DoesNotThrowAndKeepsThrottling()
    {
        _sent.Fail = true;
        var sut = Sut;

        var act = () => sut.Notify(_team);

        act.Should().NotThrow();
        sut.Notify(_team);
        _sent.Teams.Should().HaveCount(1);
    }

    private sealed class RecordingBroadcaster : IDashboardBroadcaster
    {
        public List<Guid> Teams { get; } = [];

        public bool Fail { get; set; }

        public Task SendAsync(Guid teamId, CancellationToken cancellationToken)
        {
            Teams.Add(teamId);
            return Fail ? Task.FromException(new InvalidOperationException("hub down")) : Task.CompletedTask;
        }
    }
}
