using FluentAssertions;
using IncidentHub.Application.Notifications.Email;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace IncidentHub.Application.UnitTests.Notifications.Email;

public sealed class SlaBreachedEmailHandlerTests
{
    private readonly EmailTestKit _kit = new();
    private readonly SlaBreachedEmailHandler _handler;
    private readonly Guid _incidentId = Guid.NewGuid();
    private readonly Guid _contact = Guid.NewGuid();
    private readonly Guid _team = Guid.NewGuid();

    public SlaBreachedEmailHandlerTests()
    {
        _handler = new SlaBreachedEmailHandler(_kit.Reader, _kit.Router, NullLogger<SlaBreachedEmailHandler>.Instance);
        GivenIncident(EmailTestKit.Facts(teamId: _team, status: IncidentStatus.InProgress, escalationUserId: _contact));
        _kit.Reader.GetUserAsync(_contact, Arg.Any<CancellationToken>()).Returns(new EmailRecipient("lead@example.com", "Lee"));
        _kit.Reader.GetTeamAsync(_team, Arg.Any<CancellationToken>()).Returns(new EmailRecipient("team@example.com", "Payments"));
    }

    private void GivenIncident(IncidentEmailFacts? facts) =>
        _kit.Reader.GetIncidentAsync(_incidentId, Arg.Any<CancellationToken>()).Returns(facts);

    private SlaBreached Event() => new(_incidentId, EmailTestKit.At);

    [Fact]
    public async Task Handle_WithContact_SendsToContact()
    {
        var outboxId = Guid.NewGuid();

        await _handler.HandleAsync(Event(), outboxId, CancellationToken.None);

        var sent = _kit.Sent().Should().ContainSingle().Subject;
        sent.ToAddress.Should().Be("lead@example.com");
        sent.MessageId.Should().Be(outboxId);
        sent.Subject.Should().Be("[INC-1042] SLA BREACHED: Checkout fails");
    }

    [Fact]
    public async Task Handle_NoContact_MailsOwningTeam()
    {
        GivenIncident(EmailTestKit.Facts(teamId: _team, status: IncidentStatus.InProgress));

        await _handler.HandleAsync(Event(), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().ContainSingle().Which.ToAddress.Should().Be("team@example.com");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_ContactMissingOrWithoutAddress_MailsOwningTeam(bool blankAddress)
    {
        _kit.Reader.GetUserAsync(_contact, Arg.Any<CancellationToken>())
            .Returns(blankAddress ? new EmailRecipient(" ", "Lee") : null);

        await _handler.HandleAsync(Event(), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().ContainSingle().Which.ToAddress.Should().Be("team@example.com");
    }

    [Fact]
    public async Task Handle_NoContactAndTeamWithoutAddress_Skips()
    {
        GivenIncident(EmailTestKit.Facts(teamId: _team, status: IncidentStatus.InProgress));
        _kit.Reader.GetTeamAsync(_team, Arg.Any<CancellationToken>()).Returns(new EmailRecipient("", "Payments"));

        await _handler.HandleAsync(Event(), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_IncidentGone_Skips()
    {
        GivenIncident(null);

        await _handler.HandleAsync(Event(), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ContactIsAlsoTheAssignee_StillMails()
    {
        GivenIncident(EmailTestKit.Facts(teamId: _team, status: IncidentStatus.InProgress, assigneeId: _contact, escalationUserId: _contact));

        await _handler.HandleAsync(Event(), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().ContainSingle().Which.ToAddress.Should().Be("lead@example.com");
    }

    [Fact]
    public async Task Handle_TeamsPreference_UsesChatLeg()
    {
        _kit.Reader.GetUserAsync(_contact, Arg.Any<CancellationToken>())
            .Returns(new EmailRecipient("lead@example.com", "Lee", EmailTestKit.TeamsPrefs));
        _kit.Reader.GetTeamChannelUrlAsync(_team, Arg.Any<CancellationToken>()).Returns(EmailTestKit.TeamsUrl);

        await _handler.HandleAsync(Event(), Guid.NewGuid(), CancellationToken.None);

        _kit.SentOn(_kit.TeamsSender).Should().ContainSingle();
        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoContact_TeamFallbackMailsAndPosts()
    {
        GivenIncident(EmailTestKit.Facts(teamId: _team, status: IncidentStatus.InProgress));
        _kit.Reader.GetTeamChannelUrlAsync(_team, Arg.Any<CancellationToken>()).Returns(EmailTestKit.SlackUrl);

        await _handler.HandleAsync(Event(), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().ContainSingle().Which.ToAddress.Should().Be("team@example.com");
        _kit.SentOn(_kit.SlackSender).Should().ContainSingle();
    }
}
