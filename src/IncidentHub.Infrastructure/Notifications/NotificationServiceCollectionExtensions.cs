using IncidentHub.Application.Abstractions;
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

        services.TryAddSingleton(TimeProvider.System);
        services.AddResiliencePipeline(
            NotificationResilience.EmailPipeline,
            (builder, context) => NotificationResilience.Configure(
                builder,
                context.ServiceProvider.GetRequiredService<IOptions<NotificationResilienceOptions>>().Value));

        services.TryAddSingleton<EmailTemplateRenderer>();

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

        return services;
    }
}
