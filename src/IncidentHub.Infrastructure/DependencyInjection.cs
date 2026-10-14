using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using IncidentHub.Application.Abstractions;
using IncidentHub.Infrastructure.Attachments;
using IncidentHub.Infrastructure.Caching;
using IncidentHub.Infrastructure.Outbox;
using IncidentHub.Infrastructure.Persistence;
using IncidentHub.Infrastructure.Persistence.Search;
using IncidentHub.Infrastructure.Security;
using IncidentHub.Infrastructure.Sla;
using IncidentHub.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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

        services.TryAddSingleton<IPostCommitDispatcher, NoOpPostCommitDispatcher>();
        services.AddScoped<PostCommitHook>();
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IAppTeamReader, RedisAppTeamReader>();
        services.AddScoped<IAppStatusReader, RedisAppStatusReader>();
        services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<ISimilarIncidentSearch, SqlSimilarIncidentSearch>();
        services.AddScoped<IIncidentTextSearch, SqlIncidentTextSearch>();
        services.AddSingleton<IHtmlSanitizer, HtmlSanitizerAdapter>();
        services.AddSlaPolicies(configuration);
        services.AddAttachmentStorage(configuration);
        services.AddOutbox(configuration);

        return services;
    }

    /// <summary>
    /// Blob storage, the scanner and the scan pass, and the lock the Worker takes around it. The container is created when the
    /// host starts; the options are validated then too, so a missing <c>Storage:Blob</c> setting stops the host with a clear message.
    /// </summary>
    private static void AddAttachmentStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BlobStorageOptions>()
            .Bind(configuration.GetSection(BlobStorageOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BlobStorageOptions>, BlobStorageOptionsValidator>());

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<BlobStorageOptions>>().Value;
            var clientOptions = new BlobClientOptions
            {
                Retry =
                {
                    Mode = RetryMode.Exponential,
                    MaxRetries = 3,
                    Delay = TimeSpan.FromMilliseconds(500),
                    NetworkTimeout = TimeSpan.FromSeconds(100),
                },
            };

            return string.IsNullOrWhiteSpace(options.ConnectionString)
                ? new BlobServiceClient(new Uri(options.AccountUri!), new DefaultAzureCredential(), clientOptions)
                : new BlobServiceClient(options.ConnectionString, clientOptions);
        });
        services.AddSingleton<IFileStore, AzureBlobFileStore>();
        services.AddHostedService<BlobContainerInitializer>();

        services.AddSingleton<IAttachmentScanner, StubAttachmentScanner>();
        services.AddScoped<AttachmentScanRunner>();
        services.AddScoped<OrphanAttachmentSweeper>();
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
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
