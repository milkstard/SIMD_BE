namespace IncidentHub.Application.Notifications;

/// <summary>
/// Tells a Microsoft Teams webhook URL from a Slack one. Checks the shape only (absolute https URL without credentials); the host
/// allow-list is the sender's job.
/// </summary>
public static class ChatWebhook
{
    public const string SlackHost = "hooks.slack.com";

    /// <summary>The channel the URL belongs to, or <c>null</c> when it is missing, not absolute, not https or carries user info.</summary>
    public static NotificationChannel? Classify(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return string.Equals(uri.Host, SlackHost, StringComparison.OrdinalIgnoreCase)
            ? NotificationChannel.Slack
            : NotificationChannel.Teams;
    }
}
