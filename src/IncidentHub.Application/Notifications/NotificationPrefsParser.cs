using System.Text.Json;

namespace IncidentHub.Application.Notifications;

/// <summary>Reads <c>User.NotificationPrefs</c> (<c>{"channel":"Email"|"Teams"|"Slack"}</c>).</summary>
public static class NotificationPrefsParser
{
    /// <summary>The preferred channel, or <c>null</c> for malformed JSON, a missing property or an unknown value.</summary>
    public static NotificationChannel? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, "channel", StringComparison.OrdinalIgnoreCase)
                    || property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = property.Value.GetString();
                return Enum.TryParse<NotificationChannel>(value, ignoreCase: true, out var channel)
                    && Enum.IsDefined(channel)
                    && !int.TryParse(value, out _)
                        ? channel
                        : null;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
