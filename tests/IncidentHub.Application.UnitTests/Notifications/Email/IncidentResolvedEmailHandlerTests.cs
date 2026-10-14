using FluentAssertions;
using IncidentHub.Application.Notifications;
using IncidentHub.Application.Notifications.Email;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace IncidentHub.Application.UnitTests.Notifications.Email;

public sealed class IncidentResolvedEmailHandlerTests
{
    private readonly EmailTestKit _kit = new();
    private readonly IncidentResolvedEmailHandler _handler;
    private readonly Guid _incidentId = Guid.NewGuid();
    private readonly Guid _reporter = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();

    public IncidentResolvedEmailHandlerTests()
    {
        _handler = new IncidentResolvedEmailHandler(_kit.Reader, _kit.Delivery, NullLogger<IncidentResolvedEmailHandler>.Instance);
        _kit.Reader.GetIncidentAsync(_incidentId, Arg.Any<CancellationToken>()).Returns(EmailTestKit.Facts(reporterId: _reporter));
        _kit.Reader.GetUserAsync(_reporter, Arg.Any<CancellationToken>()).Returns(new EmailRecipient("rita@example.com", "Rita"));
    }

    private IncidentTransitioned Event(IncidentStatus to, Guid? actor = null, string? note = "Fixed the gateway timeout") =>
        new(_incidentId, IncidentStatus.InProgress, to, actor ?? _actor, note, EmailTestKit.At);

    [Fact]
    public async Task Handle_ToResolved_EmailsReporter()
    {
        var outboxId = Guid.NewGuid();

        await _handler.HandleAsync(Event(IncidentStatus.Resolved), outboxId, CancellationToken.None);

        var sent = _kit.Sent().Should().ContainSingle().Subject;
        sent.ToAddress.Should().Be("rita@example.com");
        sent.MessageId.Should().Be(outboxId);
        sent.Subject.Should().StartWith("[INC-1042] Resolved");
        var facts = _kit.Renderer.ReceivedCalls().Single().GetArguments()[1].Should().BeAssignableTo<IReadOnlyList<(string Label, string Value)>>().Subject;
        facts.Should().Contain(("Note", "Fixed the gateway timeout"));
    }

    [Theory]
    [InlineData(IncidentStatus.Triaged)]
    [InlineData(IncidentStatus.InProgress)]
    [InlineData(IncidentStatus.Closed)]
    [InlineData(IncidentStatus.Reopened)]
    public async Task Handle_ToOtherStatus_SendsNothing(IncidentStatus to)
    {
        await _handler.HandleAsync(Event(to), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
        await _kit.Reader.DidNotReceiveWithAnyArgs().GetIncidentAsync(default, default);
    }

    [Fact]
    public async Task Handle_ReporterIsActor_SendsNothing()
    {
        await _handler.HandleAsync(Event(IncidentStatus.Resolved, actor: _reporter), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReporterMissing_SkipsWithoutThrowing()
    {
        _kit.Reader.GetUserAsync(_reporter, Arg.Any<CancellationToken>()).Returns((EmailRecipient?)null);

        await _handler.HandleAsync(Event(IncidentStatus.Resolved), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SenderThrows_PropagatesForRetry()
    {
        _kit.Sender.SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>()).Throws(new NotificationDeliveryException("down"));

        var act = () => _handler.HandleAsync(Event(IncidentStatus.Resolved), Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<NotificationDeliveryException>();
    }

    [Fact]
    public void EventType_IsIncidentTransitioned() => _handler.EventType.Should().Be("IncidentTransitioned");
}
