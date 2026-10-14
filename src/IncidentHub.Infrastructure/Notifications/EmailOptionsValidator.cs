using Microsoft.Extensions.Options;
using MimeKit;

namespace IncidentHub.Infrastructure.Notifications;

/// <summary>Per-field failures so a bad configuration stops the Worker at start and names the setting. A disabled sender needs nothing.</summary>
public sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        var section = EmailOptions.SectionName;

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            failures.Add($"{section}:Host is required when the email sender is enabled.");
        }

        if (options.Port is < 1 or > 65535)
        {
            failures.Add($"{section}:Port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(options.FromAddress) || !MailboxAddress.TryParse(options.FromAddress, out _))
        {
            failures.Add($"{section}:FromAddress must be a valid email address.");
        }

        if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var baseUrl)
            || (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"{section}:PublicBaseUrl must be an absolute http or https URL.");
        }

        if (!Enum.IsDefined(options.Tls))
        {
            failures.Add($"{section}:Tls must be None, StartTls or SslOnConnect.");
        }

        if (!string.IsNullOrEmpty(options.Username))
        {
            if (string.IsNullOrEmpty(options.Password))
            {
                failures.Add($"{section}:Password is required when Username is set.");
            }

            if (options.Tls == EmailTlsMode.None)
            {
                failures.Add($"{section}:Tls must be StartTls or SslOnConnect when Username is set (credentials would be sent in clear text).");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
