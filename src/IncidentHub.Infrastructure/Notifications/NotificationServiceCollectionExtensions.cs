using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Notifications;
using IncidentHub.Application.Notifications.Email;
using IncidentHub.Application.Outbox;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Infrastructure.Notifications.Chat;
using IncidentHub.Infrastructure.Notifications.Rendering;
using IncidentHub.Infrastructure.Notifications.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace IncidentHub.Infrastructure.Notifications;

public static class NotificationServiceCollectionExtensions
{
    /// <summary>
    /// The notification channels and their resilience pipelines. Called by the Worker only; the API sends nothing. Options are
    /// validated when the host starts. With <c>Notifications:Email:Enabled</c> false a logging sender is registered instead of SMTP.
    /// </summary>
    public static IServiceCollection AddNotificationSenders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>());

        services.AddOptions<NotificationResilienceOptions>()
            .Bind(configuration.GetSection(NotificationResilienceOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<NotificationResilienceOptions>, NotificationResilienceOptionsValidator>());

        services.AddOptions<ChatOptions>()
            .Bind(configuration.GetSection(ChatOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ChatOptions>, ChatOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);

        // One pipeline (and so one circuit) per channel: a broken Slack does not open the Teams or email circuit.
        foreach (var pipelineName in new[]
        {
            NotificationResilience.EmailPipeline,
            NotificationResilience.TeamsPipeline,
            NotificationResilience.SlackPipeline,
        })
        {
            services.AddResiliencePipeline(
                pipelineName,
                (builder, context) => NotificationResilience.Configure(
                    builder,
                    context.ServiceProvider.GetRequiredService<IOptions<NotificationResilienceOptions>>().Value));
        }

        services.TryAddSingleton<EmailTemplateRenderer>();
        services.TryAddSingleton<IEmailTemplateRenderer>(sp => sp.GetRequiredService<EmailTemplateRenderer>());

        // The email triggers. Keyed by the event type they handle, so the dispatcher builds only the handlers a message needs.
        services.TryAddScoped<IIncidentEmailReader, IncidentEmailReader>();
        services.TryAddScoped<EmailDelivery>();
        services.AddKeyedScoped<IOutboxMessageHandler, IncidentReportedEmailHandler>(nameof(IncidentReported));
        services.AddKeyedScoped<IOutboxMessageHandler, IncidentAssignedEmailHandler>(nameof(IncidentAssigned));
        services.AddKeyedScoped<IOutboxMessageHandler, IncidentResolvedEmailHandler>(nameof(IncidentTransitioned));
        services.AddKeyedScoped<IOutboxMessageHandler, SlaWarningEmailHandler>(nameof(SlaWarning));
        services.AddKeyedScoped<IOutboxMessageHandler, SlaBreachedEmailHandler>(nameof(SlaBreached));

        if (configuration.GetValue<bool>($"{EmailOptions.SectionName}:Enabled"))
        {
            services.TryAddSingleton<ISmtpTransport, MailKitSmtpTransport>();
            services.AddSingleton<INotificationSender>(sp => new EmailSender(
                sp.GetRequiredService<ISmtpTransport>(),
                sp.GetRequiredService<ResiliencePipelineProvider<string>>().GetPipeline(NotificationResilience.EmailPipeline),
                sp.GetRequiredService<IOptions<EmailOptions>>(),
                sp.GetRequiredService<ILogger<EmailSender>>()));
        }
        else
        {
            services.AddSingleton<INotificationSender, LoggingEmailSender>();
        }

        if (configuration.GetValue<bool>($"{ChatOptions.SectionName}:Enabled"))
        {
            // No factory logging: it writes the request URI, and the webhook URL is a credential.
            services.AddHttpClient(TeamsNotificationSender.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(ChatWebhookSender.CreatePrimaryHandler)
                .RemoveAllLoggers();
            services.AddHttpClient(SlackNotificationSender.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(ChatWebhookSender.CreatePrimaryHandler)
                .RemoveAllLoggers();

            services.AddSingleton<INotificationSender>(sp => new TeamsNotificationSender(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ResiliencePipelineProvider<string>>().GetPipeline(NotificationResilience.TeamsPipeline),
                sp.GetRequiredService<IOptions<ChatOptions>>(),
                sp.GetRequiredService<ILogger<TeamsNotificationSender>>()));
            services.AddSingleton<INotificationSender>(sp => new SlackNotificationSender(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ResiliencePipelineProvider<string>>().GetPipeline(NotificationResilience.SlackPipeline),
                sp.GetRequiredService<IOptions<ChatOptions>>(),
                sp.GetRequiredService<ILogger<SlackNotificationSender>>()));
        }
        else
        {
            services.AddSingleton<INotificationSender>(sp => new LoggingChatSender(
                NotificationChannel.Teams, sp.GetRequiredService<ILogger<LoggingChatSender>>()));
            services.AddSingleton<INotificationSender>(sp => new LoggingChatSender(
                NotificationChannel.Slack, sp.GetRequiredService<ILogger<LoggingChatSender>>()));
        }

        return services;
    }
}
