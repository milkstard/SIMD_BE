using IncidentHub.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentHub.Infrastructure.Realtime;

public static class ChangeRelayExtensions
{
    /// <summary>Worker host: publishes its committed changes for the API instances. Call after <c>AddInfrastructure</c>, it replaces the no-op dispatcher.</summary>
    public static IServiceCollection AddWorkerChangePublisher(this IServiceCollection services)
    {
        services.TryAddSingleton<ProcessOrigin>();
        services.Replace(ServiceDescriptor.Singleton<IPostCommitDispatcher, RedisChangePublisher>());
        return services;
    }

    /// <summary>API host: relays the Worker's changes into the host's <see cref="IPostCommitDispatcher"/>. Call after that dispatcher is registered.</summary>
    public static IServiceCollection AddChangeRelay(this IServiceCollection services)
    {
        services.TryAddSingleton<ProcessOrigin>();
        services.AddHostedService<RedisChangeSubscriber>();
        return services;
    }
}
