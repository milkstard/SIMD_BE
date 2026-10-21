using FluentAssertions;
using IncidentHub.Application.Notifications;

namespace IncidentHub.Application.UnitTests.Notifications;

public sealed class ChatWebhookTests
{
    [Theory]
    [InlineData("https://hooks.slack.com/services/T000/B000/XXXX")]
    [InlineData("https://HOOKS.SLACK.COM/services/T000/B000/XXXX")]
    public void Classify_SlackHost_ReturnsSlack(string url)
    {
        ChatWebhook.Classify(url).Should().Be(NotificationChannel.Slack);
    }

    [Theory]
    [InlineData("https://contoso.webhook.office.com/webhookb2/abc")]
    [InlineData("https://prod-12.westus.logic.azure.com:443/workflows/abc/triggers/manual/paths/invoke?sig=x")]
    [InlineData("https://hooks.slack.com.evil.example/services/x")]
    public void Classify_OtherHttpsHost_ReturnsTeams(string url)
    {
        ChatWebhook.Classify(url).Should().Be(NotificationChannel.Teams);
    }

    [Theory]
    [InlineData("http://hooks.slack.com/services/T000/B000/XXXX")]
    [InlineData("http://contoso.webhook.office.com/webhookb2/abc")]
    [InlineData("ftp://contoso.example/hook")]
    public void Classify_Http_ReturnsNull(string url)
    {
        ChatWebhook.Classify(url).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("https://user:secret@hooks.slack.com/services/x")]
    public void Classify_NullOrMalformed_ReturnsNull(string? url)
    {
        ChatWebhook.Classify(url).Should().BeNull();
    }
}
