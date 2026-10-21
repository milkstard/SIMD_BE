using IncidentHub.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace IncidentHub.Infrastructure.Notifications.Chat;

/// <summary>Posts <c>{"text": ...}</c> to a Slack incoming webhook.</summary>
internal sealed class SlackNotificationSender(
    IHttpClientFactory httpClientFactory,
    ResiliencePipeline pipeline,
    IOptions<ChatOptions> options,
    ILogger<SlackNotificationSender> logger) : ChatWebhookSender(httpClientFactory, pipeline, options, logger)
{
    public const string HttpClientName = "slack";

    public override NotificationChannel Channel => NotificationChannel.Slack;

    protected override string ClientName => HttpClientName;

    protected override object BuildPayload(Notification notification) => new Dictionary<string, object?>
    {
        ["text"] = $"*{Escape(notification.Subject)}*\n{Escape(notification.TextBody)}",
    };

    /// <summary>Slack's control characters, so <c>&lt;!channel&gt;</c> or <c>&lt;@U123&gt;</c> in a title pings nobody.</summary>
    internal static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
