using IncidentHub.Application.Abstractions;
using IncidentHub.Infrastructure.Caching;
using IncidentHub.Infrastructure.Persistence;
using IncidentHub.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace IncidentHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(RequireConnectionString(configuration, "Sql"));
        });

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var redisOptions = ConfigurationOptions.Parse(RequireConnectionString(configuration, "Redis"));
            redisOptions.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(redisOptions);
        });

        services.AddScoped<IAppTeamReader, RedisAppTeamReader>();
        services.AddScoped<IUserDirectory, UserDirectory>();

        return services;
    }

    /// <summary>Fails with a clear message for missing <i>or empty</i> values (appsettings ships empty placeholders).</summary>
    public static string RequireConnectionString(IConfiguration configuration, string name)
    {
        var value = configuration.GetConnectionString(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"ConnectionStrings:{name} is not configured. Set it with user-secrets or environment variables.")
            : value;
    }
}
