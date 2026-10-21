using FluentAssertions;
using IncidentHub.Application.Notifications;
using IncidentHub.Application.Notifications.Email;
using IncidentHub.Application.Outbox;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace IncidentHub.Application.UnitTests.Notifications.Email;

public sealed class NotificationRouterTests
{
    private readonly EmailTestKit _kit = new();
    private readonly FakeLogger _logger = new();
    private readonly NotificationRouter _router;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _teamId = Guid.NewGuid();
    private readonly Guid _outboxId = Guid.NewGuid();

    public NotificationRouterTests()
    {
        _router = new NotificationRouter(_kit.Delivery, _kit.Chat, _kit.Reader, _logger);
    }

    private static readonly EmailContent Content = new(
        "[INC-1042] Assigned to you: Checkout fails",
        "Assigned to you",
        [("Application", "Checkout")],
        "INC-1042");

    private static EmailRecipient User(string? prefs) => new("ana@example.com", "Ana", prefs);

    private static EmailRecipient Team => new("team@example.com", "Payments");

    private void GivenTeamUrl(string? url) =>
        _kit.Reader.GetTeamChannelUrlAsync(_teamId, Arg.Any<CancellationToken>()).Returns(url);

    private Task SendToUser(string? prefs) =>
        _router.SendToUserAsync(_outboxId, _userId, User(prefs), _teamId, Content, CancellationToken.None);

    private Task SendToTeam() =>
        _router.SendToTeamAsync(_outboxId, _teamId, Team, Content, CancellationToken.None);

    private void AssertNoUrlLogged() =>
        _logger.Messages.Should().NotContain(m => m.Contains("https://") || m.Contains("/abc") || m.Contains("XXXX"));

    [Fact]
    public async Task SendToUser_Email_SendsEmailOnly()
    {
        await SendToUser("{\"channel\":\"Email\"}");

        _kit.Sent().Should().ContainSingle().Which.ToAddress.Should().Be("ana@example.com");
        _kit.SentOn(_kit.TeamsSender).Should().BeEmpty();
        _kit.SentOn(_kit.SlackSender).Should().BeEmpty();
        await _kit.Reader.DidNotReceiveWithAnyArgs().GetTeamChannelUrlAsync(default, default);
    }

    [Fact]
    public async Task SendToUser_Teams_SendsChatOnly()
    {
        GivenTeamUrl(EmailTestKit.TeamsUrl);

        await SendToUser(EmailTestKit.TeamsPrefs);

        var posted = _kit.SentOn(_kit.TeamsSender).Should().ContainSingle().Subject;
        posted.ToAddress.Should().Be(EmailTestKit.TeamsUrl);
        posted.MessageId.Should().Be(_outboxId);
        posted.Subject.Should().Be(Content.Subject);
        _kit.Sent().Should().BeEmpty();
        _kit.SentOn(_kit.SlackSender).Should().BeEmpty();
        AssertNoUrlLogged();
    }

    [Fact]
    public async Task SendToUser_Slack_SendsChatOnly()
    {
        GivenTeamUrl(EmailTestKit.SlackUrl);

        await SendToUser(EmailTestKit.SlackPrefs);

        _kit.SentOn(_kit.SlackSender).Should().ContainSingle().Which.ToAddress.Should().Be(EmailTestKit.SlackUrl);
        _kit.Sent().Should().BeEmpty();
        _kit.SentOn(_kit.TeamsSender).Should().BeEmpty();
    }

    [Fact]
    public async Task SendToUser_TeamsPrefButSlackUrl_FallsBackToEmail()
    {
        GivenTeamUrl(EmailTestKit.SlackUrl);

        await SendToUser(EmailTestKit.TeamsPrefs);

        _kit.Sent().Should().ContainSingle();
        _kit.SentOn(_kit.TeamsSender).Should().BeEmpty();
        _kit.SentOn(_kit.SlackSender).Should().BeEmpty();
        _logger.Messages.Should().ContainSingle(m => m.Contains("using email"));
        AssertNoUrlLogged();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://contoso.webhook.office.com/x")]
    public async Task SendToUser_NoUsableUrl_FallsBackToEmail(string? url)
    {
        GivenTeamUrl(url);

        await SendToUser(EmailTestKit.TeamsPrefs);

        _kit.Sent().Should().ContainSingle();
        _kit.SentOn(_kit.TeamsSender).Should().BeEmpty();
    }

    [Fact]
    public async Task SendToUser_ChatDisabled_FallsBackToEmail()
    {
        _kit.TeamsSender.IsLive.Returns(false);
        GivenTeamUrl(EmailTestKit.TeamsUrl);

        await SendToUser(EmailTestKit.TeamsPrefs);

        _kit.Sent().Should().ContainSingle();
        _kit.SentOn(_kit.TeamsSender).Should().BeEmpty();
        _logger.Messages.Should().ContainSingle(m => m.Contains("chat is disabled"));
    }

    [Fact]
    public async Task SendToUser_ChatPermanentFailure_FallsBackToEmail()
    {
        GivenTeamUrl(EmailTestKit.TeamsUrl);
        _kit.TeamsSender.SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>())
            .Throws(new NonRetryableOutboxException("Teams webhook at contoso.webhook.office.com answered 404."));

        await SendToUser(EmailTestKit.TeamsPrefs);

        _kit.Sent().Should().ContainSingle();
        _logger.Messages.Should().ContainSingle(m => m.Contains("failed permanently"));
        AssertNoUrlLogged();
    }

    [Fact]
    public async Task SendToUser_ChatTransientFailure_PropagatesAndSendsNoEmail()
    {
        GivenTeamUrl(EmailTestKit.TeamsUrl);
        _kit.TeamsSender.SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>())
            .Throws(new NotificationDeliveryException("down"));

        var act = () => SendToUser(EmailTestKit.TeamsPrefs);

        await act.Should().ThrowAsync<NotificationDeliveryException>();
        _kit.Sent().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("{\"channel\":\"Pager\"}")]
    public async Task SendToUser_BadPrefs_EmailsAndWarns(string? prefs)
    {
        await SendToUser(prefs);

        _kit.Sent().Should().ContainSingle();
        _logger.Messages.Should().ContainSingle(m => m.Contains(_userId.ToString()) && m.Contains("unreadable"));
        if (prefs is not null)
        {
            _logger.Messages.Should().NotContain(m => m.Contains(prefs));
        }
    }

    [Fact]
    public async Task SendToTeam_WithUrl_SendsEmailThenChat()
    {
        GivenTeamUrl(EmailTestKit.SlackUrl);

        await SendToTeam();

        _kit.Sent().Should().ContainSingle().Which.ToAddress.Should().Be("team@example.com");
        _kit.SentOn(_kit.SlackSender).Should().ContainSingle().Which.ToAddress.Should().Be(EmailTestKit.SlackUrl);
        _kit.SentOn(_kit.TeamsSender).Should().BeEmpty();
    }

    [Fact]
    public async Task SendToTeam_NoUrl_SendsEmailOnly()
    {
        GivenTeamUrl(null);

        await SendToTeam();

        _kit.Sent().Should().ContainSingle();
        _kit.SentOn(_kit.TeamsSender).Should().BeEmpty();
        _kit.SentOn(_kit.SlackSender).Should().BeEmpty();
    }

    [Fact]
    public async Task SendToTeam_ChatDisabled_SendsEmailOnly()
    {
        _kit.TeamsSender.IsLive.Returns(false);
        GivenTeamUrl(EmailTestKit.TeamsUrl);

        await SendToTeam();

        _kit.Sent().Should().ContainSingle();
        _kit.SentOn(_kit.TeamsSender).Should().BeEmpty();
    }

    [Fact]
    public async Task SendToTeam_ChatPermanentFailure_Swallowed()
    {
        GivenTeamUrl(EmailTestKit.TeamsUrl);
        _kit.TeamsSender.SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>())
            .Throws(new NonRetryableOutboxException("Teams webhook at contoso.webhook.office.com answered 410."));

        await SendToTeam();

        _kit.Sent().Should().ContainSingle();
        _logger.Messages.Should().ContainSingle(m => m.Contains("failed permanently"));
        AssertNoUrlLogged();
    }

    [Fact]
    public async Task SendToTeam_ChatTransientFailure_Propagates()
    {
        GivenTeamUrl(EmailTestKit.TeamsUrl);
        _kit.TeamsSender.SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>())
            .Throws(new NotificationDeliveryException("down"));

        var act = SendToTeam;

        await act.Should().ThrowAsync<NotificationDeliveryException>();
        _kit.Sent().Should().ContainSingle("the mail goes out first");
    }

    [Fact]
    public async Task SendToTeam_EmailFails_ChatNotAttempted()
    {
        GivenTeamUrl(EmailTestKit.TeamsUrl);
        _kit.Sender.SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>())
            .Throws(new NotificationDeliveryException("smtp down"));

        var act = SendToTeam;

        await act.Should().ThrowAsync<NotificationDeliveryException>();
        _kit.SentOn(_kit.TeamsSender).Should().BeEmpty();
    }

    /// <summary>Captures the rendered text of every log entry, so a test can prove the webhook URL never reaches the log.</summary>
    private sealed class FakeLogger : ILogger<NotificationRouter>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
