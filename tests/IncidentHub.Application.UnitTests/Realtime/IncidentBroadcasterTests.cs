using FluentAssertions;
using IncidentHub.Application.Common;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Application.Realtime;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using NSubstitute;

namespace IncidentHub.Application.UnitTests.Realtime;

public sealed class IncidentBroadcasterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly IIncidentBroadcastReader _reader = Substitute.For<IIncidentBroadcastReader>();
    private readonly Guid _incidentId = Guid.NewGuid();
    private readonly Guid _teamId = Guid.NewGuid();

    private IncidentBroadcaster Sut => new(_reader);

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

    private IncidentReported Reported() =>
        new(_incidentId, Guid.NewGuid(), AppEnvironment.Production, Severity.High, IncidentSource.Manual, Guid.NewGuid(), Now);

    private IncidentTransitioned Transitioned() =>
        new(_incidentId, IncidentStatus.New, IncidentStatus.Triaged, null, null, Now);

    private void GivenIncident()
    {
        var summary = new IncidentSummaryDto(
            _incidentId, "INC-1001", "Title", new ApplicationRefDto(Guid.NewGuid(), "App", "APP"), AppEnvironment.Production,
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
