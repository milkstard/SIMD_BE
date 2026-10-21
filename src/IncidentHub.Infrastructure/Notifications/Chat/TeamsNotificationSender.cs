using System.Text;
using IncidentHub.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace IncidentHub.Infrastructure.Notifications.Chat;

/// <summary>Posts an Adaptive Card to a Teams Workflows webhook.</summary>
internal sealed class TeamsNotificationSender(
    IHttpClientFactory httpClientFactory,
    ResiliencePipeline pipeline,
    IOptions<ChatOptions> options,
    ILogger<TeamsNotificationSender> logger) : ChatWebhookSender(httpClientFactory, pipeline, options, logger)
{
    public const string HttpClientName = "teams";

    private const string MarkdownControl = "\\`*_~[]()#<>|";

    public override NotificationChannel Channel => NotificationChannel.Teams;

    protected override string ClientName => HttpClientName;

    protected override object BuildPayload(Notification notification) => new Dictionary<string, object?>
    {
        ["type"] = "message",
        ["attachments"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["contentType"] = "application/vnd.microsoft.card.adaptive",
                ["contentUrl"] = null,
                ["content"] = new Dictionary<string, object?>
                {
                    ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
                    ["type"] = "AdaptiveCard",
                    ["version"] = "1.4",
                    ["body"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "TextBlock",
                            ["text"] = EscapeMarkdown(notification.Subject),
                            ["weight"] = "Bolder",
                            ["wrap"] = true,
                        },
                        new Dictionary<string, object?>
                        {
                            ["type"] = "TextBlock",
                            ["text"] = EscapeMarkdown(notification.TextBody),
                            ["wrap"] = true,
                        },
                    },
                },
            },
        },
    };

    /// <summary>Backslash-escapes Markdown control characters so a title cannot form a link, heading or emphasis.</summary>
    internal static string EscapeMarkdown(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            if (MarkdownControl.Contains(c))
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
