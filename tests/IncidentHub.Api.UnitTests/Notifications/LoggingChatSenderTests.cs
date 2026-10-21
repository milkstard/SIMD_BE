using FluentAssertions;
using IncidentHub.Application.Notifications;
using IncidentHub.Infrastructure.Notifications.Chat;
using Microsoft.Extensions.Logging.Testing;
using static IncidentHub.Api.UnitTests.Notifications.ChatTestKit;

namespace IncidentHub.Api.UnitTests.Notifications;

public sealed class LoggingChatSenderTests
{
    [Theory]
    [InlineData(NotificationChannel.Teams, TeamsUrl, "prod-12.westus.logic.azure.com")]
    [InlineData(NotificationChannel.Slack, SlackUrl, "hooks.slack.com")]
    public async Task Send_LogsHostAndSubject_NotUrl(NotificationChannel channel, string url, string host)
    {
        var logger = new FakeLogger<LoggingChatSender>();
        var sender = new LoggingChatSender(channel, logger);

        await sender.SendAsync(Message(url, subject: "Checkout is down"), CancellationToken.None);

        sender.Channel.Should().Be(channel);
        var entry = logger.Collector.GetSnapshot().Should().ContainSingle().Subject;
        entry.Message.Should().Contain(host).And.Contain("Checkout is down");
        entry.Message.Should().NotContain(SecretPath).And.NotContain("SECRETSIG");
    }
}
