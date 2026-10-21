using System.Net;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Application.Notifications;
using IncidentHub.Application.Outbox;
using IncidentHub.Infrastructure.Notifications.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Polly;
using static IncidentHub.Api.UnitTests.Notifications.ChatTestKit;

namespace IncidentHub.Api.UnitTests.Notifications;

public sealed class TeamsNotificationSenderTests
{
    private readonly FakeWebhookHandler _handler = new();
    private readonly FakeLogger<TeamsNotificationSender> _logger = new();

    private TeamsNotificationSender Create(ChatOptions? options = null, ResiliencePipeline? pipeline = null) => new(
        new StubHttpClientFactory(_handler),
        pipeline ?? ResiliencePipeline.Empty,
        Options.Create(options ?? new ChatOptions()),
        _logger);

    [Fact]
    public async Task Send_PostsAdaptiveCard()
    {
        await Create().SendAsync(Message(TeamsUrl), CancellationToken.None);

        var request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Host.Should().Be("prod-12.westus.logic.azure.com");
        request.ContentType.Should().Be("application/json");

        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;
        root.GetProperty("type").GetString().Should().Be("message");
        var attachment = root.GetProperty("attachments").EnumerateArray().Single();
        attachment.GetProperty("contentType").GetString().Should().Be("application/vnd.microsoft.card.adaptive");
        var card = attachment.GetProperty("content");
        card.GetProperty("type").GetString().Should().Be("AdaptiveCard");
        card.GetProperty("version").GetString().Should().Be("1.4");
        var body = card.GetProperty("body").EnumerateArray().ToList();
        body.Should().HaveCount(2);
        body[0].GetProperty("type").GetString().Should().Be("TextBlock");
        body[0].GetProperty("weight").GetString().Should().Be("Bolder");
        body[1].GetProperty("wrap").GetBoolean().Should().BeTrue();
        body[1].GetProperty("text").GetString().Should().Contain("INC-1042");
    }

    [Fact]
    public async Task Send_MarkdownInTitle_IsEscaped()
    {
        await Create().SendAsync(Message(TeamsUrl, subject: "[click](http://evil.example) *bold* `x`"), CancellationToken.None);

        using var json = JsonDocument.Parse(_handler.Requests.Single().Body);
        var title = json.RootElement.GetProperty("attachments")[0].GetProperty("content").GetProperty("body")[0].GetProperty("text").GetString();
        title.Should().Be(@"\[click\]\(http://evil.example\) \*bold\* \`x\`");
    }

    [Fact]
    public async Task Send_SlackUrl_ThrowsNonRetryable()
    {
        var act = () => Create().SendAsync(Message(SlackUrl), CancellationToken.None);

        await act.Should().ThrowAsync<NonRetryableOutboxException>();
        _handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/hook")]
    [InlineData("http://prod-12.westus.logic.azure.com/workflows/x")]
    public async Task Send_InvalidOrNonHttpsUrl_ThrowsNonRetryableWithoutCall(string url)
    {
        var act = () => Create().SendAsync(Message(url), CancellationToken.None);

        await act.Should().ThrowAsync<NonRetryableOutboxException>();
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_HostNotAllowed_ThrowsNonRetryableWithoutCall()
    {
        var act = () => Create().SendAsync(Message("https://internal.example.com/hook/" + SecretPath), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<NonRetryableOutboxException>();
        thrown.Which.Message.Should().Contain("internal.example.com").And.NotContain(SecretPath);
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_HostAllowedByOperatorOverride_Posts()
    {
        var options = new ChatOptions { AllowedHosts = ["internal.example.com"] };

        await Create(options).SendAsync(Message("https://internal.example.com/hook"), CancellationToken.None);

        _handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Send_TransientStatus_ThrowsDeliveryException(HttpStatusCode status)
    {
        _handler.Always(status);

        var act = () => Create().SendAsync(Message(TeamsUrl), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<NotificationDeliveryException>();
        thrown.Which.Message.Should().Contain(((int)status).ToString());
    }

    [Fact]
    public async Task Send_Status429_ThrowsDeliveryException()
    {
        _handler.Always(HttpStatusCode.TooManyRequests);

        var act = () => Create().SendAsync(Message(TeamsUrl), CancellationToken.None);

        await act.Should().ThrowAsync<NotificationDeliveryException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task Send_PermanentStatus_ThrowsNonRetryable(HttpStatusCode status)
    {
        _handler.Always(status);

        var act = () => Create().SendAsync(Message(TeamsUrl), CancellationToken.None);

        await act.Should().ThrowAsync<NonRetryableOutboxException>();
    }

    [Fact]
    public async Task Send_Status404_ThrowsNonRetryable()
    {
        _handler.Always(HttpStatusCode.NotFound);

        var act = () => Create().SendAsync(Message(TeamsUrl), CancellationToken.None);

        await act.Should().ThrowAsync<NonRetryableOutboxException>();
    }

    [Fact]
    public async Task Send_Redirect_ThrowsNonRetryable()
    {
        _handler.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data");
            return Task.FromResult(response);
        };

        var act = () => Create().SendAsync(Message(TeamsUrl), CancellationToken.None);

        await act.Should().ThrowAsync<NonRetryableOutboxException>();
        _handler.Requests.Should().ContainSingle("a redirect is never followed");
    }

    [Fact]
    public void CreatePrimaryHandler_DoesNotFollowRedirects()
    {
        using var handler = ChatWebhookSender.CreatePrimaryHandler();

        handler.AllowAutoRedirect.Should().BeFalse();
    }

    [Fact]
    public async Task Send_NetworkError_ThrowsDeliveryExceptionWithoutInnerException()
    {
        _handler.Respond = _ => throw new HttpRequestException("Connection to " + TeamsUrl + " refused");

        var act = () => Create().SendAsync(Message(TeamsUrl), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<NotificationDeliveryException>();
        thrown.Which.InnerException.Should().BeNull();
    }

    [Fact]
    public async Task Send_ErrorText_HasNoUrl()
    {
        var texts = new List<string>();

        foreach (var respond in new Func<HttpRequestMessage, Task<HttpResponseMessage>>[]
        {
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found)),
            _ => throw new HttpRequestException("Connection to " + TeamsUrl + " refused"),
        })
        {
            _handler.Respond = respond;
            try
            {
                await Create().SendAsync(Message(TeamsUrl), CancellationToken.None);
            }
            catch (Exception exception)
            {
                texts.Add(exception.ToString());
            }
        }

        // And the success path.
        _handler.Always(HttpStatusCode.OK);
        await Create().SendAsync(Message(TeamsUrl), CancellationToken.None);

        texts.Should().HaveCount(4);
        texts.Should().OnlyContain(t => !t.Contains(SecretPath) && !t.Contains("SECRETSIG"));
        _logger.Collector.GetSnapshot().Should().OnlyContain(r => !r.Message.Contains(SecretPath) && !r.Message.Contains("SECRETSIG"));
        _logger.Collector.GetSnapshot().Should().Contain(r => r.Level == LogLevel.Information && r.Message.Contains("prod-12.westus.logic.azure.com"));
    }
}
