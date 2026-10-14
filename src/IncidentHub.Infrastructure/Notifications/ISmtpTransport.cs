using MimeKit;

namespace IncidentHub.Infrastructure.Notifications;

/// <summary>The one seam over the SMTP client, so <see cref="EmailSender"/> is unit-testable without a server.</summary>
internal interface ISmtpTransport
{
    Task SendAsync(MimeMessage message, CancellationToken cancellationToken);
}
