using FluentValidation;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Behaviors;
using IncidentHub.Application.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentHub.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly);

        services.AddScoped<IIncidentBroadcastReader, IncidentBroadcastReader>();
        services.AddScoped<IncidentBroadcaster>();
        services.TryAddScoped<IDashboardSummaryReader, NullDashboardSummaryReader>();

        return services;
    }
}
