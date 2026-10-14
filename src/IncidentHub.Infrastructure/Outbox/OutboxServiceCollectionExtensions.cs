using IncidentHub.Application.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IncidentHub.Infrastructure.Outbox;

public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// The dispatch pass and its options (validated when the host starts). Handlers register themselves as
    /// <see cref="IOutboxMessageHandler"/>; only the Worker hosts the loop.
    /// </summary>
    public static IServiceCollection AddOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OutboxDispatcherOptions>()
            .Bind(configuration.GetSection(OutboxDispatcherOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OutboxDispatcherOptions>, OutboxDispatcherOptionsValidator>());

        services.TryAddSingleton<OutboxEventTypes>();
        services.TryAddScoped<IOutboxDispatchRunner, OutboxDispatchRunner>();
        return services;
    }
}
