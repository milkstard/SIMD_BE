using System.Net;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Application.Notifications;
using IncidentHub.Application.Outbox;
using IncidentHub.Infrastructure.Notifications.Chat;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Polly;
using static IncidentHub.Api.UnitTests.Notifications.ChatTestKit;

namespace IncidentHub.Api.UnitTests.Notifications;

public sealed class SlackNotificationSenderTests
{
    private readonly FakeWebhookHandler _handler = new();

    private SlackNotificationSender Create(ResiliencePipeline? pipeline = null) => new(
        new StubHttpClientFactory(_handler),
        pipeline ?? ResiliencePipeline.Empty,
        Options.Create(new ChatOptions()),
        new FakeLogger<SlackNotificationSender>());

    private string PostedText()
    {
        using var json = JsonDocument.Parse(_handler.Requests.Single().Body);
        return json.RootElement.GetProperty("text").GetString()!;
    }

    [Fact]
    public async Task Send_PostsTextPayload()
    {
        await Create().SendAsync(Message(SlackUrl), CancellationToken.None);

        var request = _handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Host.Should().Be("hooks.slack.com");
        request.ContentType.Should().Be("application/json");
        PostedText().Should().StartWith("*[High] INC-1042 Checkout fails*\n").And.Contain("Application: Checkout");
    }

    [Fact]
    public async Task Send_ControlCharactersInTitle_AreEscaped()
    {
        await Create().SendAsync(Message(SlackUrl, subject: "<!channel> <@U1> R&D"), CancellationToken.None);

        PostedText().Should().StartWith("*&lt;!channel&gt; &lt;@U1&gt; R&amp;D*");
    }

    [Fact]
    public async Task Send_TeamsUrl_ThrowsNonRetryable()
    {
        var act = () => Create().SendAsync(Message(TeamsUrl), CancellationToken.None);

        await act.Should().ThrowAsync<NonRetryableOutboxException>();
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_Status500_ThrowsDeliveryException()
    {
        _handler.Always(HttpStatusCode.InternalServerError);

        var act = () => Create().SendAsync(Message(SlackUrl), CancellationToken.None);

        await act.Should().ThrowAsync<NotificationDeliveryException>();
    }

    [Fact]
    public async Task Send_Status403_ThrowsNonRetryable()
    {
        _handler.Always(HttpStatusCode.Forbidden);

        var act = () => Create().SendAsync(Message(SlackUrl), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<NonRetryableOutboxException>();
        thrown.Which.Message.Should().Contain("403").And.NotContain(SecretPath);
    }
}
