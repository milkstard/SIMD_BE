using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Sla;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IncidentHub.Infrastructure.Sla;

public static class SlaServiceCollectionExtensions
{
    /// <summary>Binds <c>Sla:Defaults</c>, validates it when the host starts and registers the table-backed policy provider (<c>Sla:Defaults</c> is the fallback for a missing row).</summary>
    public static IServiceCollection AddSlaPolicies(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SlaDefaultsOptions>()
            .Bind(configuration.GetSection(SlaDefaultsOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SlaDefaultsOptions>, SlaDefaultsOptionsValidator>());
        services.AddScoped<ISlaPolicyProvider, DbSlaPolicyProvider>();

        return services;
    }
}
