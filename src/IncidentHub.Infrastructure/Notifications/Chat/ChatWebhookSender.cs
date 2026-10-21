using System.Text;
using System.Text.Json;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Notifications;
using IncidentHub.Application.Outbox;
using IncidentHub.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace IncidentHub.Infrastructure.Notifications.Chat;

/// <summary>
/// The only types that call a chat webhook. Validates the destination, posts the channel's payload through the channel's resilience
/// pipeline and maps every failure to <see cref="NotificationDeliveryException"/> (retryable) or <see cref="NonRetryableOutboxException"/>
/// (permanent).
/// <para>
/// The webhook URL is a credential (anyone holding it can post to the channel), so no error text, log entry or span tag carries it:
/// only the channel, the status code and the destination <i>host</i>, and never an inner <see cref="HttpRequestException"/>.
/// </para>
/// </summary>
internal abstract class ChatWebhookSender(
    IHttpClientFactory httpClientFactory,
    ResiliencePipeline pipeline,
    IOptions<ChatOptions> options,
    ILogger logger) : INotificationSender
{
    /// <summary>Set on every request so the OpenTelemetry HTTP instrumentation skips it (its span would carry the full URL).</summary>
    public static readonly HttpRequestOptionsKey<bool> SuppressTracing = new("incidenthub.suppress-tracing");

    private readonly ChatOptions _options = options.Value;

    public abstract NotificationChannel Channel { get; }

    public bool IsLive => true;

    /// <summary>The named <see cref="HttpClient"/> of this channel.</summary>
    protected abstract string ClientName { get; }

    protected abstract object BuildPayload(Notification notification);

    internal static SocketsHttpHandler CreatePrimaryHandler() => new() { AllowAutoRedirect = false };

    public async Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("notification.send");
        var host = HostOf(notification.ToAddress);
        activity?.SetTag("channel", Channel.ToString().ToLowerInvariant());
        activity?.SetTag("recipient.host", host);

        var destination = Validate(notification.ToAddress, host);
        var json = JsonSerializer.Serialize(BuildPayload(notification));

        try
        {
            await pipeline.ExecuteAsync(token => PostAsync(destination, json, host, token), cancellationToken);
        }
        catch (BrokenCircuitException exception)
        {
            throw new NotificationDeliveryException($"{Channel} circuit is open; the send was not attempted.", exception);
        }
        catch (TimeoutRejectedException exception)
        {
            throw new NotificationDeliveryException($"{Channel} webhook call to {host} timed out.", exception);
        }

        logger.LogInformation("{Channel} message {OutboxMessageId} posted to a webhook at {RecipientHost}", Channel, notification.MessageId, host);
    }

    private Uri Validate(string address, string host)
    {
        if (ChatWebhook.Classify(address) != Channel)
        {
            throw new NonRetryableOutboxException($"The {Channel} destination ({host}) is missing, not an https URL, or not a {Channel} webhook.");
        }

        var uri = new Uri(address.Trim());
        if (!HostAllowList.Allows(uri.Host, _options.EffectiveAllowedHosts))
        {
            throw new NonRetryableOutboxException($"The {Channel} webhook host {host} is not in {ChatOptions.SectionName}:AllowedHosts.");
        }

        return uri;
    }

    private async ValueTask PostAsync(Uri destination, string json, string host, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, destination)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Options.Set(SuppressTracing, true);

        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = (int)response.StatusCode;

            if (status is >= 200 and < 300)
            {
                return;
            }

            var text = $"{Channel} webhook at {host} answered {status}.";
            throw status is 408 or 429 or >= 500
                ? new NotificationDeliveryException(text)
                : new NonRetryableOutboxException(text);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or NotificationDeliveryException or NonRetryableOutboxException))
        {
            // No inner exception: HttpRequestException messages and stack frames can embed the request URI.
            throw new NotificationDeliveryException($"{Channel} webhook call to {host} failed: {exception.GetType().Name}.");
        }
    }

    private static string HostOf(string address) =>
        Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host) ? uri.Host : "unknown";
}
