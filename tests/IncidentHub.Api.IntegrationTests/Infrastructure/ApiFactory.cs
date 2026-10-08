using IncidentHub.Api.Auth;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Teams;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace IncidentHub.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API against Testcontainers SQL Server + Redis. Requests authenticate either with the header-driven
/// <see cref="TestAuthHandler"/> (when <c>X-Test-Oid</c> is present) or with real JWT validation against a local signing key.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string PolicyScheme = "TestDispatch";

    private static readonly Dictionary<UserRole, string> GroupIds = Enum.GetValues<UserRole>()
        .ToDictionary(r => r, _ => Guid.NewGuid().ToString());

    private readonly MsSqlContainer _sql = new MsSqlBuilder().Build();
    private readonly RedisContainer _redis = new RedisBuilder().Build();

    public string TenantId { get; } = Guid.NewGuid().ToString();

    public string ClientId { get; } = Guid.NewGuid().ToString();

    public TestJwt Jwt { get; } = new();

    public string GroupFor(UserRole role) => GroupIds[role];

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _redis.StartAsync());

        // Forces the host to build, then applies the real migrations to the container database.
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
        await _redis.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // UseSetting is visible to eager configuration reads in Program.cs, unlike ConfigureAppConfiguration.
        builder.UseSetting("ConnectionStrings:Sql", _sql.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
        builder.UseSetting("AzureAd:TenantId", TenantId);
        builder.UseSetting("AzureAd:ClientId", ClientId);
        foreach (var (role, groupId) in GroupIds)
        {
            builder.UseSetting($"Authorization:GroupRoleMap:{groupId}", role.ToString());
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(AuthProbeController).Assembly);

            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = PolicyScheme;
                    options.DefaultAuthenticateScheme = PolicyScheme;
                    options.DefaultChallengeScheme = PolicyScheme;
                    options.DefaultForbidScheme = PolicyScheme;
                })
                .AddPolicyScheme(PolicyScheme, PolicyScheme, options =>
                    options.ForwardDefaultSelector = context =>
                        context.Request.Headers.ContainsKey(TestAuthHandler.OidHeader)
                            ? TestAuthHandler.SchemeName
                            : JwtBearerDefaults.AuthenticationScheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.AddSingleton(this);

            // Real JWT validation, but against a local key instead of Entra's metadata endpoint.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Jwt.Issuer(TenantId) };
                configuration.SigningKeys.Add(Jwt.SigningKey);
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);

                var parameters = options.TokenValidationParameters;
                parameters.ValidIssuer = Jwt.Issuer(TenantId);
                parameters.IssuerValidator = null;
                parameters.IssuerSigningKey = Jwt.SigningKey;
                parameters.ValidAudiences = [$"api://{ClientId}", ClientId];
                parameters.AudienceValidator = null;
            });
        });
    }

    /// <summary>Creates a team and a monitored application owned by it; returns the application id.</summary>
    public async Task<Guid> CreateAppAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var team = Team.Create($"team-{suffix}", $"{suffix}@test.local", null);
        var app = MonitoredApp.Create($"app-{suffix}", suffix[..12], team.Id, null, [AppEnvironment.Production]);
        db.Teams.Add(team);
        db.Applications.Add(app);
        await db.SaveChangesAsync();
        return app.Id;
    }

    /// <summary>Makes the user (identified by Entra object id) a member of the application's owning team.</summary>
    public async Task AddToTeamAsync(string objectId, Guid applicationId)
    {
        using var scope = Services.CreateScope();
        var userId = await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
            .EnsureUserAsync(objectId, "Test User", $"{objectId}@test.local", CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teamId = await db.Applications.Where(a => a.Id == applicationId).Select(a => a.OwningTeamId).SingleAsync();
        db.TeamMembers.Add(TeamMember.Create(teamId, userId, UserRole.Responder));
        await db.SaveChangesAsync();
    }

    /// <summary>Returns the internal user id for an Entra object id, creating the user when needed.</summary>
    public async Task<Guid> EnsureUserAsync(string objectId)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
            .EnsureUserAsync(objectId, "Test User", $"{objectId}@test.local", CancellationToken.None);
    }

    /// <summary>Connection string for a fresh, empty database on the shared SQL container.</summary>
    public string NewDatabaseConnectionString()
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_sql.GetConnectionString())
        {
            InitialCatalog = $"catalog_{Guid.NewGuid():N}",
        };
        return builder.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
