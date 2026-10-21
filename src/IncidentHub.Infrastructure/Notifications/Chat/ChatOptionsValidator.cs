using Microsoft.Extensions.Options;

namespace IncidentHub.Infrastructure.Notifications.Chat;

/// <summary>Per-field failures so a bad configuration stops the Worker at start and names the setting. Disabled chat needs nothing.</summary>
public sealed class ChatOptionsValidator : IValidateOptions<ChatOptions>
{
    public ValidateOptionsResult Validate(string? name, ChatOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        var field = $"{ChatOptions.SectionName}:AllowedHosts";

        if (options.EffectiveAllowedHosts.Count == 0)
        {
            failures.Add($"{field} must not be empty when chat is enabled.");
        }

        foreach (var pattern in options.EffectiveAllowedHosts.Where(p => !HostAllowList.IsValidPattern(p)))
        {
            failures.Add($"{field} has an invalid entry '{pattern}': use a host name or '*.' followed by a domain (for example '*.example.com').");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
