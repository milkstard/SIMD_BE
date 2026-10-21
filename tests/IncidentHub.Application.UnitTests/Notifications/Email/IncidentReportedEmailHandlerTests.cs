using FluentAssertions;
using IncidentHub.Application.Notifications;
using IncidentHub.Application.Notifications.Email;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace IncidentHub.Application.UnitTests.Notifications.Email;

public sealed class IncidentReportedEmailHandlerTests
{
    private readonly EmailTestKit _kit = new();
    private readonly IncidentReportedEmailHandler _handler;
    private readonly IncidentReported _event = new(
        Guid.NewGuid(), Guid.NewGuid(), AppEnvironment.Production, Severity.High, IncidentSource.Manual, Guid.NewGuid(), EmailTestKit.At);

    public IncidentReportedEmailHandlerTests()
    {
        _handler = new IncidentReportedEmailHandler(_kit.Reader, _kit.Router, NullLogger<IncidentReportedEmailHandler>.Instance);
    }

    [Fact]
    public async Task Handle_Reported_EmailsOwningTeam()
    {
        var outboxId = Guid.NewGuid();
        var facts = EmailTestKit.Facts();
        _kit.Reader.GetIncidentAsync(_event.IncidentId, Arg.Any<CancellationToken>()).Returns(facts);
        _kit.Reader.GetTeamAsync(facts.OwningTeamId, Arg.Any<CancellationToken>()).Returns(new EmailRecipient("team@example.com", "Payments"));

        await _handler.HandleAsync(_event, outboxId, CancellationToken.None);

        var sent = _kit.Sent().Should().ContainSingle().Subject;
        sent.ToAddress.Should().Be("team@example.com");
        sent.MessageId.Should().Be(outboxId);
        sent.Subject.Should().StartWith("[INC-1042] High incident reported");
    }

    [Fact]
    public async Task Handle_TeamMissing_SkipsWithoutThrowing()
    {
        _kit.Reader.GetIncidentAsync(_event.IncidentId, Arg.Any<CancellationToken>()).Returns(EmailTestKit.Facts());
        _kit.Reader.GetTeamAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((EmailRecipient?)null);

        await _handler.HandleAsync(_event, Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TeamWithoutAddress_SkipsWithoutThrowing()
    {
        _kit.Reader.GetIncidentAsync(_event.IncidentId, Arg.Any<CancellationToken>()).Returns(EmailTestKit.Facts());
        _kit.Reader.GetTeamAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new EmailRecipient("  ", "Payments"));

        await _handler.HandleAsync(_event, Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_IncidentMissing_SkipsWithoutThrowing()
    {
        _kit.Reader.GetIncidentAsync(_event.IncidentId, Arg.Any<CancellationToken>()).Returns((IncidentEmailFacts?)null);

        await _handler.HandleAsync(_event, Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SenderThrows_PropagatesForRetry()
    {
        _kit.Reader.GetIncidentAsync(_event.IncidentId, Arg.Any<CancellationToken>()).Returns(EmailTestKit.Facts());
        _kit.Reader.GetTeamAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new EmailRecipient("team@example.com", "Payments"));
        _kit.Sender.SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>()).Throws(new NotificationDeliveryException("down"));

        var act = () => _handler.HandleAsync(_event, Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<NotificationDeliveryException>();
    }

    [Fact]
    public void EventType_IsIncidentReported() => _handler.EventType.Should().Be("IncidentReported");

    [Fact]
    public async Task Handle_TeamWithWebhook_MailsAndPostsToChannel()
    {
        var facts = EmailTestKit.Facts();
        _kit.Reader.GetIncidentAsync(_event.IncidentId, Arg.Any<CancellationToken>()).Returns(facts);
        _kit.Reader.GetTeamAsync(facts.OwningTeamId, Arg.Any<CancellationToken>()).Returns(new EmailRecipient("team@example.com", "Payments"));
        _kit.Reader.GetTeamChannelUrlAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(EmailTestKit.TeamsUrl);

        await _handler.HandleAsync(_event, Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().ContainSingle().Which.ToAddress.Should().Be("team@example.com");
        _kit.SentOn(_kit.TeamsSender).Should().ContainSingle().Which.ToAddress.Should().Be(EmailTestKit.TeamsUrl);
    }
}
