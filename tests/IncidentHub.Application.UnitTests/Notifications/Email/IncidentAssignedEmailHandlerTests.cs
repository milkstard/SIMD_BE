using FluentAssertions;
using IncidentHub.Application.Notifications;
using IncidentHub.Application.Notifications.Email;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace IncidentHub.Application.UnitTests.Notifications.Email;

public sealed class IncidentAssignedEmailHandlerTests
{
    private readonly EmailTestKit _kit = new();
    private readonly IncidentAssignedEmailHandler _handler;
    private readonly Guid _incidentId = Guid.NewGuid();
    private readonly Guid _assignee = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();

    public IncidentAssignedEmailHandlerTests()
    {
        _handler = new IncidentAssignedEmailHandler(_kit.Reader, _kit.Delivery, NullLogger<IncidentAssignedEmailHandler>.Instance);
        _kit.Reader.GetIncidentAsync(_incidentId, Arg.Any<CancellationToken>()).Returns(EmailTestKit.Facts(assigneeId: _assignee));
        _kit.Reader.GetUserAsync(_assignee, Arg.Any<CancellationToken>()).Returns(new EmailRecipient("ana@example.com", "Ana"));
    }

    private IncidentAssigned Event(Guid? assignee, Guid? previous, Guid? team = null, Guid? previousTeam = null) =>
        new(_incidentId, assignee, team, previous, previousTeam, _actor, EmailTestKit.At);

    [Fact]
    public async Task Handle_NewAssignee_SendsOnce()
    {
        var outboxId = Guid.NewGuid();

        await _handler.HandleAsync(Event(_assignee, null), outboxId, CancellationToken.None);

        var sent = _kit.Sent().Should().ContainSingle().Subject;
        sent.ToAddress.Should().Be("ana@example.com");
        sent.MessageId.Should().Be(outboxId);
        sent.Subject.Should().StartWith("[INC-1042] Assigned to you");
    }

    [Fact]
    public async Task Handle_ChangedAssignee_SendsToTheNewOne()
    {
        await _handler.HandleAsync(Event(_assignee, Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_SameAssignee_SendsNothing()
    {
        await _handler.HandleAsync(Event(_assignee, _assignee, team: Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Unassign_SendsNothing()
    {
        await _handler.HandleAsync(Event(null, _assignee), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SelfAssignment_SendsNothing()
    {
        await _handler.HandleAsync(Event(_actor, null), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CurrentAssigneeDiffers_SendsNothing()
    {
        _kit.Reader.GetIncidentAsync(_incidentId, Arg.Any<CancellationToken>()).Returns(EmailTestKit.Facts(assigneeId: Guid.NewGuid()));

        await _handler.HandleAsync(Event(_assignee, null), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AssigneeMissing_SkipsWithoutThrowing()
    {
        _kit.Reader.GetUserAsync(_assignee, Arg.Any<CancellationToken>()).Returns((EmailRecipient?)null);

        await _handler.HandleAsync(Event(_assignee, null), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_IncidentMissing_SkipsWithoutThrowing()
    {
        _kit.Reader.GetIncidentAsync(_incidentId, Arg.Any<CancellationToken>()).Returns((IncidentEmailFacts?)null);

        await _handler.HandleAsync(Event(_assignee, null), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SenderThrows_PropagatesForRetry()
    {
        _kit.Sender.SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>()).Throws(new NotificationDeliveryException("down"));

        var act = () => _handler.HandleAsync(Event(_assignee, null), Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<NotificationDeliveryException>();
    }
}
