using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Users;

public sealed class User : Entity
{
    public const string DefaultNotificationPrefs = "{\"channel\":\"Email\"}";

    private User()
    {
    }

    public string EntraObjectId { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public DateTimeOffset LastSeenAt { get; private set; }

    public string NotificationPrefs { get; private set; } = DefaultNotificationPrefs;

    public static User Create(string entraObjectId, string displayName, string email, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entraObjectId);

        return new User
        {
            EntraObjectId = entraObjectId,
            DisplayName = displayName,
            Email = email,
            LastSeenAt = now,
        };
    }

    public void Touch(string displayName, string email, DateTimeOffset now)
    {
        DisplayName = displayName;
        Email = email;
        LastSeenAt = now;
    }
}
