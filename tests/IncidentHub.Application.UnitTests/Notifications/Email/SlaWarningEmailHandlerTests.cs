using FluentAssertions;
using IncidentHub.Application.Notifications.Email;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace IncidentHub.Application.UnitTests.Notifications.Email;

public sealed class SlaWarningEmailHandlerTests
{
    private readonly EmailTestKit _kit = new();
    private readonly SlaWarningEmailHandler _handler;
    private readonly Guid _incidentId = Guid.NewGuid();
    private readonly Guid _assignee = Guid.NewGuid();

    public SlaWarningEmailHandlerTests()
    {
        _handler = new SlaWarningEmailHandler(_kit.Reader, _kit.Router, NullLogger<SlaWarningEmailHandler>.Instance);
        GivenIncident(EmailTestKit.Facts(status: IncidentStatus.InProgress, assigneeId: _assignee, slaWarnedAt: EmailTestKit.At));
        _kit.Reader.GetUserAsync(_assignee, Arg.Any<CancellationToken>()).Returns(new EmailRecipient("ana@example.com", "Ana"));
    }

    private void GivenIncident(IncidentEmailFacts? facts) =>
        _kit.Reader.GetIncidentAsync(_incidentId, Arg.Any<CancellationToken>()).Returns(facts);

    private SlaWarning Event() => new(_incidentId, EmailTestKit.At);

    [Fact]
    public async Task Handle_Assigned_SendsToAssignee()
    {
        var outboxId = Guid.NewGuid();

        await _handler.HandleAsync(Event(), outboxId, CancellationToken.None);

        var sent = _kit.Sent().Should().ContainSingle().Subject;
        sent.ToAddress.Should().Be("ana@example.com");
        sent.MessageId.Should().Be(outboxId);
        sent.Subject.Should().Be("[INC-1042] SLA warning: Checkout fails");
    }

    [Fact]
    public async Task Handle_Unassigned_Skips()
    {
        GivenIncident(EmailTestKit.Facts(status: IncidentStatus.InProgress, slaWarnedAt: EmailTestKit.At));

        await _handler.HandleAsync(Event(), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Theory]
    [InlineData(IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Closed)]
    public async Task Handle_IncidentNoLongerOpen_Skips(IncidentStatus status)
    {
        GivenIncident(EmailTestKit.Facts(status: status, assigneeId: _assignee, slaWarnedAt: EmailTestKit.At));

        await _handler.HandleAsync(Event(), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WarningCleared_Skips()
    {
        GivenIncident(EmailTestKit.Facts(status: IncidentStatus.InProgress, assigneeId: _assignee, slaWarnedAt: null));

        await _handler.HandleAsync(Event(), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WarningIsAnOlderOne_Skips()
    {
        GivenIncident(EmailTestKit.Facts(status: IncidentStatus.InProgress, assigneeId: _assignee, slaWarnedAt: EmailTestKit.At.AddHours(1)));

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_AssigneeMissingOrWithoutAddress_Skips(bool blankAddress)
    {
        _kit.Reader.GetUserAsync(_assignee, Arg.Any<CancellationToken>())
            .Returns(blankAddress ? new EmailRecipient("  ", "Ana") : null);

        await _handler.HandleAsync(Event(), Guid.NewGuid(), CancellationToken.None);

        _kit.Sent().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TeamsPreference_UsesChatLeg()
    {
        _kit.Reader.GetUserAsync(_assignee, Arg.Any<CancellationToken>())
            .Returns(new EmailRecipient("ana@example.com", "Ana", EmailTestKit.TeamsPrefs));
        _kit.Reader.GetTeamChannelUrlAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(EmailTestKit.TeamsUrl);

        await _handler.HandleAsync(Event(), Guid.NewGuid(), CancellationToken.None);

        _kit.SentOn(_kit.TeamsSender).Should().ContainSingle();
        _kit.Sent().Should().BeEmpty();
    }
}
