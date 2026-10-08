using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Teams;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Persistence;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogPersistenceTests(ApiFactory factory)
{
    private const string PreviousMigration = "20261007051533_AddUsersAndAppTeams";

    [Fact]
    public async Task Migrate_EmptyDatabase_Succeeds()
    {
        await using var db = NewIsolatedContext();

        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Migrate_FromAddUsersAndAppTeams_Succeeds()
    {
        await using var db = NewIsolatedContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Schema_CatalogTables_MatchDatabaseDoc()
    {
        var connectionString = factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Sql"];
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var columns = await ReadColumnsAsync(connection);
        var indexes = await ReadIndexesAsync(connection);

        columns["Applications.Name"].Should().Be("nvarchar(200) NOT NULL");
        columns["Applications.Code"].Should().Be("nvarchar(20) NOT NULL");
        columns["Applications.OwningTeamId"].Should().Be("uniqueidentifier NOT NULL");
        columns["Applications.EscalationUserId"].Should().Be("uniqueidentifier NULL");
        columns["Applications.Environments"].Should().Be("nvarchar(max) NOT NULL");
        columns["Applications.IsActive"].Should().Be("bit NOT NULL");
        columns["Applications.RowVersion"].Should().Be("timestamp NOT NULL");
        columns["Teams.Name"].Should().Be("nvarchar(200) NOT NULL");
        columns["Teams.Email"].Should().Be("nvarchar(320) NOT NULL");
        columns["Teams.TeamsChannelUrl"].Should().Be("nvarchar(500) NULL");
        columns["Teams.RowVersion"].Should().Be("timestamp NOT NULL");
        columns["TeamMembers.TeamId"].Should().Be("uniqueidentifier NOT NULL");
        columns["TeamMembers.UserId"].Should().Be("uniqueidentifier NOT NULL");
        columns["TeamMembers.Role"].Should().Be("nvarchar(30) NOT NULL");
        columns["Users.NotificationPrefs"].Should().Be("nvarchar(max) NOT NULL");

        indexes.Should().Contain("Applications:IX_Applications_Code:unique");
        indexes.Should().Contain("TeamMembers:PK_TeamMembers:unique");
        indexes.Should().Contain("TeamMembers:IX_TeamMembers_UserId:nonunique");
        indexes.Should().Contain("Users:IX_Users_EntraObjectId:unique");
    }

    [Fact]
    public async Task SaveApplication_DuplicateCode_Throws()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var team = Team.Create(Unique("team"), "t@test.local", null);
        var code = Unique("c")[..10];
        db.Teams.Add(team);
        db.Applications.Add(MonitoredApp.Create("One", code, team.Id, null, [AppEnvironment.Production]));
        db.Applications.Add(MonitoredApp.Create("Two", code.ToLowerInvariant(), team.Id, null, [AppEnvironment.UAT]));

        var save = () => db.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SaveApplication_Environments_RoundTripsAsJson()
    {
        var (appId, _) = await SeedAsync([AppEnvironment.UAT, AppEnvironment.Production, AppEnvironment.Development]);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == appId);
        var raw = await db.Database
            .SqlQuery<string>($"SELECT Environments AS Value FROM Applications WHERE Id = {appId}")
            .SingleAsync();

        reloaded.Environments.Should().Equal(AppEnvironment.UAT, AppEnvironment.Production, AppEnvironment.Development);
        raw.Should().Be("[\"UAT\",\"Production\",\"Development\"]");
    }

    [Fact]
    public async Task SaveTeamAndMember_Reload_ReturnsIdenticalValues()
    {
        var userId = await EnsureUserAsync();
        var team = Team.Create(Unique("team"), "team@test.local", "https://hooks.test/abc");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Teams.Add(team);
            db.TeamMembers.Add(TeamMember.Create(team.Id, userId, UserRole.TeamLead));
            await db.SaveChangesAsync();
        }

        using var readScope = factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloadedTeam = await readDb.Teams.AsNoTracking().SingleAsync(t => t.Id == team.Id);
        var member = await readDb.TeamMembers.AsNoTracking().SingleAsync(m => m.TeamId == team.Id);

        reloadedTeam.Should().BeEquivalentTo(team, o => o.Excluding(t => t.RowVersion));
        reloadedTeam.RowVersion.Should().NotBeEmpty();
        member.UserId.Should().Be(userId);
        member.Role.Should().Be(UserRole.TeamLead);
    }

    [Fact]
    public async Task UpdateApplication_StaleRowVersion_ThrowsConcurrency()
    {
        var (appId, _) = await SeedAsync([AppEnvironment.Production]);
        using var scopeA = factory.Services.CreateScope();
        using var scopeB = factory.Services.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        var appA = await dbA.Applications.SingleAsync(a => a.Id == appId);
        var appB = await dbB.Applications.SingleAsync(a => a.Id == appId);
        var originalVersion = appA.RowVersion.ToArray();

        appA.Update("Renamed", appA.Code, appA.OwningTeamId, null, appA.Environments, true);
        await dbA.SaveChangesAsync();
        appB.Update("Stale", appB.Code, appB.OwningTeamId, null, appB.Environments, true);
        var staleSave = () => dbB.SaveChangesAsync();

        appA.RowVersion.Should().NotEqual(originalVersion);
        await staleSave.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task IsMemberAsync_UserInOwningTeam_ReturnsTrue()
    {
        var (appId, _) = await SeedAsync([AppEnvironment.Production]);
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var userId = await EnsureUserAsync(oid);

        var isMember = await IsMemberAsync(userId, appId);

        isMember.Should().BeTrue();
    }

    [Fact]
    public async Task IsMemberAsync_UserInOtherTeam_ReturnsFalse()
    {
        var (appId, _) = await SeedAsync([AppEnvironment.Production]);
        var otherAppId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, otherAppId);
        var userId = await EnsureUserAsync(oid);

        var isMember = await IsMemberAsync(userId, appId);

        isMember.Should().BeFalse();
    }

    [Fact]
    public async Task GetApplicationIdsForUserAsync_MemberOfTeam_ReturnsAllTeamApps()
    {
        var (firstId, teamId) = await SeedAsync([AppEnvironment.Production]);
        Guid secondId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var second = MonitoredApp.Create("Second", Unique("c")[..10], teamId, null, [AppEnvironment.UAT]);
            db.Applications.Add(second);
            await db.SaveChangesAsync();
            secondId = second.Id;
        }

        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, firstId);
        var userId = await EnsureUserAsync(oid);

        using var readScope = factory.Services.CreateScope();
        var ids = await readScope.ServiceProvider.GetRequiredService<IAppTeamReader>()
            .GetApplicationIdsForUserAsync(userId, CancellationToken.None);

        ids.Should().BeEquivalentTo([firstId, secondId]);
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private AppDbContext NewIsolatedContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(factory.NewDatabaseConnectionString()).Options);

    private async Task<(Guid AppId, Guid TeamId)> SeedAsync(AppEnvironment[] environments)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var team = Team.Create(Unique("team"), "t@test.local", null);
        var app = MonitoredApp.Create(Unique("app"), Unique("c")[..10], team.Id, null, environments);
        db.Teams.Add(team);
        db.Applications.Add(app);
        await db.SaveChangesAsync();
        return (app.Id, team.Id);
    }

    private async Task<Guid> EnsureUserAsync(string? objectId = null)
    {
        objectId ??= Guid.NewGuid().ToString();
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
            .EnsureUserAsync(objectId, "Test User", $"{objectId}@test.local", CancellationToken.None);
    }

    private async Task<bool> IsMemberAsync(Guid userId, Guid appId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAppTeamReader>()
            .IsMemberAsync(userId, appId, CancellationToken.None);
    }

    private static async Task<Dictionary<string, string>> ReadColumnsAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TABLE_NAME + '.' + COLUMN_NAME,
                   DATA_TYPE + CASE WHEN CHARACTER_MAXIMUM_LENGTH IS NULL THEN ''
                                    WHEN CHARACTER_MAXIMUM_LENGTH = -1 THEN '(max)'
                                    ELSE '(' + CAST(CHARACTER_MAXIMUM_LENGTH AS varchar(10)) + ')' END
                   + CASE IS_NULLABLE WHEN 'YES' THEN ' NULL' ELSE ' NOT NULL' END
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME IN ('Applications', 'Teams', 'TeamMembers', 'Users')
            """;
        var result = new Dictionary<string, string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    private static async Task<List<string>> ReadIndexesAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OBJECT_NAME(object_id) + ':' + name + ':' + CASE is_unique WHEN 1 THEN 'unique' ELSE 'nonunique' END
            FROM sys.indexes
            WHERE name IS NOT NULL AND OBJECT_NAME(object_id) IN ('Applications', 'Teams', 'TeamMembers', 'Users')
            """;
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}
