using FluentAssertions;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common;
using IncidentHub.Application.Dashboard.Dtos;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Application.Realtime;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace IncidentHub.Application.UnitTests.Realtime;

public sealed class IncidentBroadcasterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly IIncidentBroadcastReader _reader = Substitute.For<IIncidentBroadcastReader>();
    private readonly IAppStatusReader _statuses = Substitute.For<IAppStatusReader>();
    private readonly Guid _appId = Guid.NewGuid();
    private readonly Guid _incidentId = Guid.NewGuid();
    private readonly Guid _teamId = Guid.NewGuid();

    private IncidentBroadcaster Sut => new(_reader, _statuses, NullLogger<IncidentBroadcaster>.Instance);

    [Fact]
    public async Task Map_IncidentReported_TargetsDashboardAndTeam()
    {
        GivenIncident();

        var plan = await Sut.PlanAsync([Reported()], default);

        var message = plan.Messages.Should().ContainSingle().Which.Should().BeOfType<IncidentCreatedMessage>().Subject;
        message.Targets.Should().BeEquivalentTo(
        [
            new BroadcastTarget(BroadcastAudience.Dashboard, _teamId),
            new BroadcastTarget(BroadcastAudience.Team, _teamId),
        ]);
        plan.DashboardTeamIds.Should().Equal(_teamId);
    }

    [Fact]
    public async Task Map_Transitioned_TargetsIncidentGroupToo()
    {
        GivenIncident();

        var plan = await Sut.PlanAsync([Transitioned()], default);

        var message = plan.Messages.Should().ContainSingle().Which.Should().BeOfType<IncidentUpdatedMessage>().Subject;
        message.Targets.Should().BeEquivalentTo(
        [
            new BroadcastTarget(BroadcastAudience.Dashboard, _teamId),
            new BroadcastTarget(BroadcastAudience.Incident, _incidentId),
            new BroadcastTarget(BroadcastAudience.Team, _teamId),
        ]);
    }

    [Fact]
    public async Task Map_TwoUpdatedEvents_CoalescesToOne()
    {
        GivenIncident();
        IDomainEvent[] events = [Transitioned(), new IncidentUpdated(_incidentId, ["Title"], null, Now), new IncidentAssigned(_incidentId, null, null, null, null, null, Now)];

        var plan = await Sut.PlanAsync(events, default);

        plan.Messages.Should().ContainSingle().Which.Should().BeOfType<IncidentUpdatedMessage>();
        await _reader.Received(1).GetIncidentAsync(_incidentId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Map_ReportedAndUpdatedInOneSave_SendsCreatedOnly()
    {
        GivenIncident();

        var plan = await Sut.PlanAsync([Reported(), Transitioned()], default);

        plan.Messages.Should().ContainSingle().Which.Should().BeOfType<IncidentCreatedMessage>();
    }

    [Fact]
    public async Task Map_PublicComment_TargetsIncidentGroup()
    {
        var comment = GivenComment(isInternal: false);

        var plan = await Sut.PlanAsync([new CommentAdded(_incidentId, comment.Id, false, null, Now)], default);

        var message = plan.Messages.Should().ContainSingle().Which.Should().BeOfType<CommentAddedMessage>().Subject;
        message.Targets.Should().Equal(new BroadcastTarget(BroadcastAudience.Incident, _incidentId));
        message.Event.Should().Be(new CommentAddedEvent(_incidentId, comment));
    }

    [Fact]
    public async Task Map_InternalComment_TargetsRespondersOnly()
    {
        var comment = GivenComment(isInternal: true);

        var plan = await Sut.PlanAsync([new CommentAdded(_incidentId, comment.Id, true, null, Now)], default);

        var message = plan.Messages.Should().ContainSingle().Which.Should().BeOfType<CommentAddedMessage>().Subject;
        message.Targets.Should().Equal(new BroadcastTarget(BroadcastAudience.Responders, _teamId));
    }

    [Fact]
    public async Task Map_StoredCommentInternalButEventSaysPublic_TargetsRespondersOnly()
    {
        var comment = GivenComment(isInternal: true);

        var plan = await Sut.PlanAsync([new CommentAdded(_incidentId, comment.Id, false, null, Now)], default);

        var message = plan.Messages.Should().ContainSingle().Which.Should().BeOfType<CommentAddedMessage>().Subject;
        message.Targets.Should().Equal(new BroadcastTarget(BroadcastAudience.Responders, _teamId));
    }

    [Fact]
    public async Task Map_UnknownIncident_SendsNothing()
    {
        _reader.GetIncidentAsync(_incidentId, Arg.Any<CancellationToken>()).Returns((BroadcastIncident?)null);

        var plan = await Sut.PlanAsync([Reported(), Transitioned(), new CommentAdded(_incidentId, Guid.NewGuid(), false, null, Now)], default);

        plan.Messages.Should().BeEmpty();
        plan.DashboardTeamIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Map_AttachmentAdded_SendsNothing()
    {
        var plan = await Sut.PlanAsync([new AttachmentAdded(_incidentId, Guid.NewGuid(), null, false, null, Now)], default);

        plan.Messages.Should().BeEmpty();
        await _reader.DidNotReceiveWithAnyArgs().GetIncidentAsync(default, default);
    }

    [Fact]
    public async Task IncidentBroadcaster_StatusChanged_PlansAppStatusMessage()
    {
        GivenIncident();
        GivenRefresh(AppStatus.Operational, AppStatus.Outage);

        var plan = await Sut.PlanAsync([Reported()], default);

        var message = plan.Messages.OfType<AppStatusChangedMessage>().Should().ContainSingle().Subject;
        message.Item.Status.Should().Be(AppStatus.Outage);
        message.Targets.Should().Equal(new BroadcastTarget(BroadcastAudience.Dashboard, _teamId));
        plan.DashboardTeamIds.Should().Equal(_teamId);
    }

    [Fact]
    public async Task IncidentBroadcaster_NothingCachedBefore_PlansAppStatusMessage()
    {
        GivenIncident();
        GivenRefresh(null, AppStatus.Operational);

        var plan = await Sut.PlanAsync([Transitioned()], default);

        plan.Messages.OfType<AppStatusChangedMessage>().Should().ContainSingle();
    }

    [Fact]
    public async Task IncidentBroadcaster_StatusRefreshThrows_StillPlansIncidentMessage()
    {
        GivenIncident();
        _statuses.RefreshAsync(_appId, Arg.Any<CancellationToken>()).Returns<AppStatusRefresh?>(_ => throw new InvalidOperationException("sql down"));

        var plan = await Sut.PlanAsync([Reported()], default);

        plan.Messages.Should().ContainSingle().Which.Should().BeOfType<IncidentCreatedMessage>();
    }

    [Fact]
    public async Task IncidentBroadcaster_StatusRefreshCancelled_Propagates()
    {
        GivenIncident();
        _statuses.RefreshAsync(_appId, Arg.Any<CancellationToken>()).Returns<AppStatusRefresh?>(_ => throw new OperationCanceledException());

        var act = () => Sut.PlanAsync([Reported()], default);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task IncidentBroadcaster_StatusUnchanged_PlansNoAppStatusMessage()
    {
        GivenIncident();
        GivenRefresh(AppStatus.Degraded, AppStatus.Degraded);

        var plan = await Sut.PlanAsync([Transitioned()], default);

        plan.Messages.Should().NotContain(m => m is AppStatusChangedMessage);
        await _statuses.Received(1).RefreshAsync(_appId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IncidentBroadcaster_AssignmentOrTitleEdit_DoesNotRefreshStatus()
    {
        GivenIncident();

        await Sut.PlanAsync(
            [new IncidentAssigned(_incidentId, null, null, null, null, null, Now), new IncidentUpdated(_incidentId, ["Title"], null, Now)],
            default);

        await _statuses.DidNotReceiveWithAnyArgs().RefreshAsync(default, default);
    }

    [Fact]
    public async Task IncidentBroadcaster_ApplicationMoved_RefreshesBothApplications()
    {
        GivenIncident();
        var oldApp = Guid.NewGuid();

        await Sut.PlanAsync([new IncidentUpdated(_incidentId, ["ApplicationId"], null, Now, oldApp)], default);

        await _statuses.Received(1).RefreshAsync(_appId, Arg.Any<CancellationToken>());
        await _statuses.Received(1).RefreshAsync(oldApp, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IncidentBroadcaster_SlaBreach_SendsIncidentUpdatedAndLeavesAppStatusAlone()
    {
        GivenIncident();

        var plan = await Sut.PlanAsync([new IncidentUpdated(_incidentId, ["IsSlaBreached"], null, Now), new SlaBreached(_incidentId, Now)], default);

        var message = plan.Messages.Should().ContainSingle().Which.Should().BeOfType<IncidentUpdatedMessage>().Subject;
        message.Targets.Should().Contain(new BroadcastTarget(BroadcastAudience.Incident, _incidentId));
        plan.DashboardTeamIds.Should().Equal(_teamId);
        await _statuses.DidNotReceiveWithAnyArgs().RefreshAsync(default, default);
    }

    [Fact]
    public async Task IncidentBroadcaster_ReportedAndTransitioned_RefreshesOnce()
    {
        GivenIncident();

        await Sut.PlanAsync([Reported(), Transitioned()], default);

        await _statuses.Received(1).RefreshAsync(_appId, Arg.Any<CancellationToken>());
    }

    private void GivenRefresh(AppStatus? previous, AppStatus current) =>
        _statuses.RefreshAsync(_appId, Arg.Any<CancellationToken>()).Returns(new AppStatusRefresh(
            _teamId,
            previous,
            new AppStatusItemDto(new ApplicationRefDto(_appId, "App", "APP"), current, 1, Severity.High, Now)));

    private IncidentReported Reported() =>
        new(_incidentId, Guid.NewGuid(), AppEnvironment.Production, Severity.High, IncidentSource.Manual, Guid.NewGuid(), Now);

    private IncidentTransitioned Transitioned() =>
        new(_incidentId, IncidentStatus.New, IncidentStatus.Triaged, null, null, Now);

    private void GivenIncident()
    {
        var summary = new IncidentSummaryDto(
            _incidentId, "INC-1001", "Title", new ApplicationRefDto(_appId, "App", "APP"), AppEnvironment.Production,
            Severity.High, null, IncidentStatus.New, null, null, new UserRefDto(Guid.NewGuid(), "R", "r@test.local"),
            Now, Now, null, null, false, 0, "AAAA");
        _reader.GetIncidentAsync(_incidentId, Arg.Any<CancellationToken>()).Returns(new BroadcastIncident(summary, _teamId));
    }

    private CommentDto GivenComment(bool isInternal)
    {
        GivenIncident();
        var comment = new CommentDto(
            Guid.NewGuid(), _incidentId, new UserRefDto(Guid.NewGuid(), "A", "a@test.local"), "<p>hi</p>", isInternal, [], Now, null);
        _reader.GetCommentAsync(comment.Id, Arg.Any<CancellationToken>()).Returns(comment);
        return comment;
    }
}
