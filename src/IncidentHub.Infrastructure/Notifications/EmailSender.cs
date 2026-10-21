using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Notifications;
using IncidentHub.Application.Outbox;
using IncidentHub.Infrastructure.Telemetry;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace IncidentHub.Infrastructure.Notifications;

/// <summary>
/// The only type that talks SMTP. Builds the message, sends it through the <c>email</c> resilience pipeline and maps every failure to
/// <see cref="NotificationDeliveryException"/> (retryable) or <see cref="NonRetryableOutboxException"/> (permanent).
/// <para>
/// Error texts are stored in <c>OutboxMessages.Error</c> and logged, so they carry the SMTP status code and the recipient's
/// <i>domain</i> only: never the full address, the server's reply text, or an inner <see cref="SmtpCommandException"/>.
/// </para>
/// </summary>
internal sealed class EmailSender(
    ISmtpTransport transport,
    ResiliencePipeline pipeline,
    IOptions<EmailOptions> options,
    ILogger<EmailSender> logger) : INotificationSender
{
    // Authentication replies are a configuration problem a fix resolves, so they stay retryable instead of parking every message.
    private static readonly int[] RetryableFiveXx = [530, 534, 535, 538];

    private readonly EmailOptions _options = options.Value;

    public NotificationChannel Channel => NotificationChannel.Email;

    public bool IsLive => true;

    public async Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("notification.send");
        var domain = DomainOf(notification.ToAddress);
        activity?.SetTag("channel", "email");
        activity?.SetTag("recipient.domain", domain);

        var message = Build(notification, domain);

        try
        {
            await pipeline.ExecuteAsync(
                async token =>
                {
                    try
                    {
                        await transport.SendAsync(message, token);
                    }
                    catch (Exception exception) when (Map(exception, domain) is { } mapped)
                    {
                        throw mapped;
                    }
                },
                cancellationToken);
        }
        catch (BrokenCircuitException exception)
        {
            throw new NotificationDeliveryException("Email circuit is open; the send was not attempted.", exception);
        }
        catch (TimeoutRejectedException exception)
        {
            throw new NotificationDeliveryException("Email send timed out.", exception);
        }

        logger.LogInformation("Email {OutboxMessageId} sent to a recipient at {RecipientDomain}", notification.MessageId, domain);
    }

    internal MimeMessage Build(Notification notification, string domain)
    {
        if (!MailboxAddress.TryParse(notification.ToAddress.Trim(), out var recipient))
        {
            throw new NonRetryableOutboxException($"The recipient address at {domain} is not a valid email address.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(StripLineBreaks(_options.FromName), _options.FromAddress));
        message.To.Add(new MailboxAddress(StripLineBreaks(notification.ToName ?? string.Empty), recipient.Address));
        message.Subject = StripLineBreaks(notification.Subject);
        message.MessageId = $"{notification.MessageId}@incidenthub";
        message.Headers.Add("X-IncidentHub-Message", notification.MessageId.ToString());
        message.Body = new BodyBuilder { TextBody = notification.TextBody, HtmlBody = notification.HtmlBody }.ToMessageBody();
        return message;
    }

    private static Exception? Map(Exception exception, string domain) => exception switch
    {
        OperationCanceledException or NotificationDeliveryException or NonRetryableOutboxException => null,
        SmtpCommandException command when IsPermanent((int)command.StatusCode) => new NonRetryableOutboxException(
            $"SMTP {(int)command.StatusCode} ({command.ErrorCode}) for recipient at {domain}."),
        SmtpCommandException command => new NotificationDeliveryException(
            $"SMTP {(int)command.StatusCode} ({command.ErrorCode}) for recipient at {domain}."),
        ParseException => new NonRetryableOutboxException($"The message for the recipient at {domain} could not be built."),
        // No inner exception: MailKit's network and TLS messages can carry the server's reply text, the host or an address, and the
        // dispatcher logs the whole exception chain.
        _ => new NotificationDeliveryException($"SMTP send failed: {exception.GetType().Name}."),
    };

    private static bool IsPermanent(int statusCode) => statusCode >= 500 && !RetryableFiveXx.Contains(statusCode);

    private static string DomainOf(string address)
    {
        var at = address.LastIndexOf('@');
        return at >= 0 && at < address.Length - 1 ? address[(at + 1)..].Trim() : "unknown";
    }

    private static string StripLineBreaks(string value) => value.Replace("\r", string.Empty).Replace("\n", " ");
}
