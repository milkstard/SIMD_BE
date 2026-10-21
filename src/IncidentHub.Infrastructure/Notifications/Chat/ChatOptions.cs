using IncidentHub.Application.Notifications;

namespace IncidentHub.Infrastructure.Notifications.Chat;

public sealed class ChatOptions
{
    public const string SectionName = "Notifications:Chat";

    /// <summary>
    /// Hosts a webhook URL may point at when <see cref="AllowedHosts"/> is not configured. A team admin controls the URL, so the
    /// sender refuses anything else. Teams webhook hosts vary by tenant setup; an operator overrides the list in configuration.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultAllowedHosts =
    [
        ChatWebhook.SlackHost,
        "*.webhook.office.com",
        "*.logic.azure.com",
        "*.api.powerplatform.com",
    ];

    /// <summary>When false <see cref="LoggingChatSender"/> is used and nothing is posted, so dev and CI need no webhook.</summary>
    public bool Enabled { get; set; }

    /// <summary>Exact hosts or <c>*.suffix</c> patterns. Empty means <see cref="DefaultAllowedHosts"/>.</summary>
    public string[] AllowedHosts { get; set; } = [];

    public IReadOnlyList<string> EffectiveAllowedHosts => AllowedHosts.Length > 0 ? AllowedHosts : DefaultAllowedHosts;
}
