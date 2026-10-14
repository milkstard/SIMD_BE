using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace IncidentHub.Infrastructure.Notifications;

/// <summary>One short-lived <see cref="SmtpClient"/> per send (no pooling in v1).</summary>
internal sealed class MailKitSmtpTransport(IOptions<EmailOptions> options) : ISmtpTransport
{
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(5);

    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        // Bounds every network read/write that does not take the token, so a stalled server cannot outlast the pipeline's timeouts.
        using var client = new SmtpClient { Timeout = (int)IoTimeout.TotalMilliseconds };
        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            _options.Tls switch
            {
                EmailTlsMode.StartTls => SecureSocketOptions.StartTls,
                EmailTlsMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
                _ => SecureSocketOptions.None,
            },
            cancellationToken);

        if (!string.IsNullOrEmpty(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);

        try
        {
            // Bounded on its own: the send is done, so the pipeline token no longer applies, but a hung QUIT must not hold the batch.
            using var goodbye = new CancellationTokenSource(DisconnectTimeout);
            await client.DisconnectAsync(true, goodbye.Token);
        }
        catch (Exception)
        {
            // The message is already accepted; a failed goodbye must not turn it into a failed send (and a resend).
        }
    }
}
