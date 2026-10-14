namespace IncidentHub.Infrastructure.Notifications;

public sealed class EmailOptions
{
    public const string SectionName = "Notifications:Email";

    /// <summary>When false a <see cref="LoggingEmailSender"/> is used and nothing is sent, so dev and CI need no SMTP server.</summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 1025;

    /// <summary>
    /// How the connection is secured. <see cref="EmailTlsMode.None"/> is for a local catcher such as Mailpit only: the validator
    /// refuses it when a <see cref="Username"/> is set, so credentials never cross the network in clear text.
    /// </summary>
    public EmailTlsMode Tls { get; set; } = EmailTlsMode.None;

    /// <summary>Optional. Credentials come from user-secrets or Key Vault, never from appsettings.</summary>
    public string? Username { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "IncidentHub";

    /// <summary>Base URL of the web app, used for incident links (<c>{PublicBaseUrl}/incidents/{number}</c>).</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
}

public enum EmailTlsMode
{
    /// <summary>Plain text (local catcher only).</summary>
    None,

    /// <summary>Upgrade with STARTTLS (usually port 587).</summary>
    StartTls,

    /// <summary>TLS from the first byte (usually port 465).</summary>
    SslOnConnect,
}
