using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Persistence;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IncidentPersistenceTests(ApiFactory factory)
{
    private const string PreviousMigration = "20261008141639_AddCatalog";

    private static readonly DateTimeOffset T0 = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    // ---- Migration ----

    [Fact]
    public async Task Migrate_EmptyDatabase_Succeeds()
    {
        await using var db = NewIsolatedContext();

        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Migrate_FromAddCatalog_Succeeds()
    {
        await using var db = NewIsolatedContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task MigrateDown_ToAddCatalog_RemovesTablesSequenceAndFullTextCatalog()
    {
        await using var db = NewIsolatedContext();
        await db.Database.MigrateAsync();

        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        (await ScalarAsync(db, "SELECT COUNT(*) FROM sys.tables WHERE name IN ('Incidents', 'IncidentHistory')")).Should().Be(0);
        (await ScalarAsync(db, "SELECT COUNT(*) FROM sys.sequences WHERE name = 'IncidentNumbers'")).Should().Be(0);
        (await ScalarAsync(db, "SELECT COUNT(*) FROM sys.fulltext_catalogs WHERE name = 'ftIncidentHub'")).Should().Be(0);
        await db.Database.EnsureDeletedAsync();
    }

    // ---- Schema ----

    [Fact]
    public async Task Schema_IncidentTables_MatchDatabaseDoc()
    {
        await using var db = NewContext();

        var columns = await ReadColumnsAsync(db);
        var indexes = await ReadIndexesAsync(db);

        columns["Incidents.Number"].Should().Be("int NOT NULL");
        columns["Incidents.ApplicationId"].Should().Be("uniqueidentifier NOT NULL");
        columns["Incidents.Environment"].Should().Be("nvarchar(30) NOT NULL");
        columns["Incidents.Title"].Should().Be("nvarchar(300) NOT NULL");
        columns["Incidents.Description"].Should().Be("nvarchar(max) NOT NULL");
        columns["Incidents.StepsToReproduce"].Should().Be("nvarchar(max) NULL");
        columns["Incidents.Severity"].Should().Be("nvarchar(20) NOT NULL");
        columns["Incidents.Priority"].Should().Be("nvarchar(20) NULL");
        columns["Incidents.Status"].Should().Be("nvarchar(20) NOT NULL");
        columns["Incidents.Source"].Should().Be("nvarchar(20) NOT NULL");
        columns["Incidents.ReporterId"].Should().Be("uniqueidentifier NOT NULL");
        columns["Incidents.AssigneeId"].Should().Be("uniqueidentifier NULL");
        columns["Incidents.AssignedTeamId"].Should().Be("uniqueidentifier NULL");
        columns["Incidents.Fingerprint"].Should().Be("nvarchar(300) NULL");
        columns["Incidents.ExternalId"].Should().Be("nvarchar(200) NULL");
        columns["Incidents.AckDueAt"].Should().Be("datetimeoffset NULL");
        columns["Incidents.ResolveDueAt"].Should().Be("datetimeoffset NULL");
        columns["Incidents.IsSlaBreached"].Should().Be("bit NOT NULL");
        columns["Incidents.AcknowledgedAt"].Should().Be("datetimeoffset NULL");
        columns["Incidents.ResolvedAt"].Should().Be("datetimeoffset NULL");
        columns["Incidents.ClosedAt"].Should().Be("datetimeoffset NULL");
        columns["Incidents.CloseReason"].Should().Be("nvarchar(30) NULL");
        columns["Incidents.DuplicateOfId"].Should().Be("uniqueidentifier NULL");
        columns["Incidents.CreatedAt"].Should().Be("datetimeoffset NOT NULL");
        columns["Incidents.UpdatedAt"].Should().Be("datetimeoffset NOT NULL");
        columns["Incidents.RowVersion"].Should().Be("timestamp NOT NULL");
        columns["IncidentHistory.IncidentId"].Should().Be("uniqueidentifier NOT NULL");
        columns["IncidentHistory.Field"].Should().Be("nvarchar(50) NOT NULL");
        columns["IncidentHistory.OldValue"].Should().Be("nvarchar(max) NULL");
        columns["IncidentHistory.NewValue"].Should().Be("nvarchar(max) NULL");
        columns["IncidentHistory.Note"].Should().Be("nvarchar(1000) NULL");
        columns["IncidentHistory.ActorId"].Should().Be("uniqueidentifier NULL");
        columns["IncidentHistory.CreatedAt"].Should().Be("datetimeoffset NOT NULL");

        // name | unique | key columns | included columns | filter
        indexes.Should().Contain(i => i.StartsWith("IX_Incidents_Status_Severity|False|Status,Severity|ApplicationId,AssigneeId|"));
        indexes.Should().Contain(i => i.StartsWith("IX_Incidents_Application_CreatedAt|False|ApplicationId,CreatedAt||"));
        indexes.Should().Contain(i => i.StartsWith("IX_Incidents_Number|True|Number||"));
        indexes.Should().Contain(i => i.StartsWith("IX_Incidents_Fingerprint_Open|True|Fingerprint||")
            && i.Contains("[Status]<>'Closed'") && i.Contains("[Fingerprint] IS NOT NULL"));
        indexes.Should().Contain(i => i.StartsWith("IX_IncidentHistory_Incident_CreatedAt|False|IncidentId,CreatedAt||"));

        (await ScalarAsync(db, "SELECT CAST(start_value AS int) FROM sys.sequences WHERE name = 'IncidentNumbers'")).Should().Be(1001);
        (await ScalarAsync(db, "SELECT COUNT(*) FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.Incidents')")).Should().Be(1);
    }

    // ---- Numbers, round trip ----

    [Fact]
    public async Task SaveIncident_Twice_AssignsConsecutiveNumbers()
    {
        var seed = await SeedAsync();
        await using var db = NewContext();
        var first = NewIncident(seed, Unique("first"));
        var second = NewIncident(seed, Unique("second"));

        db.Incidents.Add(first);
        await db.SaveChangesAsync();
        db.Incidents.Add(second);
        await db.SaveChangesAsync();

        first.Number.Should().BeGreaterThanOrEqualTo(1001);
        second.Number.Should().Be(first.Number + 1);
    }

    [Fact]
    public async Task SaveIncident_RoundTrip_PreservesValuesAndHistory()
    {
        var seed = await SeedAsync();
        var incident = NewIncident(seed, Unique("round-trip"));
        incident.Assign(seed.ResponderId, null, seed.Responder, T0.AddMinutes(1));
        incident.Update(new IncidentChanges { Priority = Priority.P2, Environment = AppEnvironment.UAT }, seed.Responder, T0.AddMinutes(2));
        incident.ChangeSeverity(Severity.Critical, "outage", T0.AddMinutes(17), T0.AddHours(4), T0.AddHours(3), seed.Responder, T0.AddMinutes(3));
        await using (var db = NewContext())
        {
            db.Incidents.Add(incident);
            await db.SaveChangesAsync();
        }

        await using var readDb = NewContext();
        var reloaded = await readDb.Incidents.AsNoTracking().Include(i => i.History).SingleAsync(i => i.Id == incident.Id);
        var rawStatus = await readDb.Database
            .SqlQuery<string>($"SELECT Status AS Value FROM Incidents WHERE Id = {incident.Id}").SingleAsync();
        var rawSeverity = await readDb.Database
            .SqlQuery<string>($"SELECT Severity AS Value FROM Incidents WHERE Id = {incident.Id}").SingleAsync();

        reloaded.Should().BeEquivalentTo(incident, o => o
            .Excluding(i => i.RowVersion).Excluding(i => i.History).Excluding(i => i.DomainEvents));
        reloaded.RowVersion.Should().NotBeEmpty();
        // Rows written by one call share a timestamp, so break ties by field to compare a stable order.
        reloaded.History.OrderBy(h => h.CreatedAt).ThenBy(h => h.Field).Should().BeEquivalentTo(
            incident.History.OrderBy(h => h.CreatedAt).ThenBy(h => h.Field), o => o.WithStrictOrdering());
        reloaded.History.Select(h => h.Field).Should().Contain(["Status", "AssigneeId", "Priority", "Environment", "Severity"]);
        rawStatus.Should().Be("New");
        rawSeverity.Should().Be("Critical");
    }

    // ---- Fingerprint ----

    [Fact]
    public async Task SaveIncident_SameFingerprintBothOpen_ThrowsDuplicateIncident()
    {
        var seed = await SeedAsync();
        var title = Unique("duplicate");
        await using (var db = NewContext())
        {
            db.Incidents.Add(NewIncident(seed, title));
            await db.SaveChangesAsync();
        }

        await using var secondDb = NewContext();
        secondDb.Incidents.Add(NewIncident(seed, title.ToUpperInvariant()));
        var save = () => secondDb.SaveChangesAsync();

        await save.Should().ThrowAsync<DuplicateIncidentException>();
    }

    [Fact]
    public async Task SaveIncident_SameFingerprintFirstClosed_Succeeds()
    {
        var seed = await SeedAsync();
        var title = Unique("reopened-title");
        var closed = NewIncident(seed, title);
        closed.TransitionTo(IncidentStatus.Closed, seed.Responder, "invalid", T0.AddMinutes(1), CloseReason.Invalid);
        await using (var db = NewContext())
        {
            db.Incidents.Add(closed);
            await db.SaveChangesAsync();
        }

        await using var secondDb = NewContext();
        secondDb.Incidents.Add(NewIncident(seed, title));
        var save = () => secondDb.SaveChangesAsync();

        await save.Should().NotThrowAsync();
    }

    // ---- Transitions, concurrency, append-only ----

    [Fact]
    public async Task TransitionTo_Persisted_WritesIncidentAndHistoryTogether()
    {
        var seed = await SeedAsync();
        var incident = NewIncident(seed, Unique("transition"));
        await using (var db = NewContext())
        {
            db.Incidents.Add(incident);
            await db.SaveChangesAsync();
        }

        byte[] versionBefore;
        await using (var db = NewContext())
        {
            var loaded = await db.Incidents.Include(i => i.History).SingleAsync(i => i.Id == incident.Id);
            versionBefore = loaded.RowVersion.ToArray();
            loaded.TransitionTo(IncidentStatus.Triaged, seed.Responder, "confirmed", T0.AddMinutes(5));
            await db.SaveChangesAsync();
        }

        await using var readDb = NewContext();
        var reloaded = await readDb.Incidents.AsNoTracking().Include(i => i.History).SingleAsync(i => i.Id == incident.Id);
        reloaded.Status.Should().Be(IncidentStatus.Triaged);
        reloaded.AcknowledgedAt.Should().Be(T0.AddMinutes(5));
        reloaded.RowVersion.Should().NotEqual(versionBefore);
        reloaded.History.Should().HaveCount(2);
        reloaded.History.Should().Contain(h =>
            h.Field == "Status" && h.OldValue == "New" && h.NewValue == "Triaged" && h.Note == "confirmed" && h.ActorId == seed.ResponderId);
    }

    [Fact]
    public async Task UpdateIncident_StaleRowVersion_ThrowsConcurrency()
    {
        var seed = await SeedAsync();
        var incident = NewIncident(seed, Unique("stale"));
        await using (var db = NewContext())
        {
            db.Incidents.Add(incident);
            await db.SaveChangesAsync();
        }

        await using var dbA = NewContext();
        await using var dbB = NewContext();
        var a = await dbA.Incidents.SingleAsync(i => i.Id == incident.Id);
        var b = await dbB.Incidents.SingleAsync(i => i.Id == incident.Id);

        a.Assign(seed.ResponderId, null, seed.Responder, T0.AddMinutes(1));
        await dbA.SaveChangesAsync();
        b.Update(new IncidentChanges { Title = Unique("stale-edit") }, seed.Responder, T0.AddMinutes(2));
        var staleSave = () => dbB.SaveChangesAsync();

        await staleSave.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task ModifyHistoryRow_Tracked_ThrowsBeforeSave()
    {
        var seed = await SeedAsync();
        var incident = NewIncident(seed, Unique("append-only-modify"));
        await using (var db = NewContext())
        {
            db.Incidents.Add(incident);
            await db.SaveChangesAsync();
        }

        await using var tamperDb = NewContext();
        var row = await tamperDb.IncidentHistory.SingleAsync(h => h.IncidentId == incident.Id);
        tamperDb.Entry(row).State = EntityState.Modified;
        var save = () => tamperDb.SaveChangesAsync();

        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*append-only*");
    }

    [Fact]
    public async Task DeleteHistoryRow_Tracked_ThrowsBeforeSave()
    {
        var seed = await SeedAsync();
        var incident = NewIncident(seed, Unique("append-only-delete"));
        await using (var db = NewContext())
        {
            db.Incidents.Add(incident);
            await db.SaveChangesAsync();
        }

        await using var tamperDb = NewContext();
        var row = await tamperDb.IncidentHistory.SingleAsync(h => h.IncidentId == incident.Id);
        tamperDb.IncidentHistory.Remove(row);
        var save = () => tamperDb.SaveChangesAsync();

        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*append-only*");
    }

    // ---- Full-text ----

    [Fact]
    public async Task FullTextSearch_Title_ReturnsMatch()
    {
        var seed = await SeedAsync();
        var token = $"fts{Guid.NewGuid():N}";
        await using (var db = NewContext())
        {
            db.Incidents.Add(NewIncident(seed, $"Checkout {token} fails"));
            await db.SaveChangesAsync();
        }

        await using var searchDb = NewContext();
        var found = 0;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (found == 0 && DateTimeOffset.UtcNow < deadline)
        {
            found = await searchDb.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM Incidents WHERE CONTAINS(Title, {token})")
                .SingleAsync();
            if (found == 0)
            {
                await Task.Delay(500);
            }
        }

        found.Should().Be(1);
    }

    // ---- Helpers ----

    private sealed record Seed(Guid AppId, Guid ReporterId, Actor Reporter, Guid ResponderId, Actor Responder);

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private static Incident NewIncident(Seed seed, string title) =>
        Incident.Report(
            seed.AppId,
            AppEnvironment.Production,
            Severity.High,
            title,
            "<p>Users see an error page.</p>",
            "1. Pay",
            IncidentSource.Manual,
            seed.Reporter,
            T0);

    private async Task<Seed> SeedAsync()
    {
        var appId = await factory.CreateAppAsync();
        var reporterId = await factory.EnsureUserAsync(Guid.NewGuid().ToString());
        var responderId = await factory.EnsureUserAsync(Guid.NewGuid().ToString());
        return new Seed(
            appId,
            reporterId,
            new Actor(reporterId, new HashSet<UserRole> { UserRole.Reporter }),
            responderId,
            new Actor(responderId, new HashSet<UserRole> { UserRole.Responder }));
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Sql"])
            .Options);

    private AppDbContext NewIsolatedContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(factory.NewDatabaseConnectionString()).Options);

    private static Task<int> ScalarAsync(AppDbContext db, string sql) =>
        db.Database.SqlQueryRaw<int>("SELECT (" + sql + ") AS Value").SingleAsync();

    private static async Task<Dictionary<string, string>> ReadColumnsAsync(AppDbContext db)
    {
        var rows = await db.Database.SqlQueryRaw<string>("""
            SELECT TABLE_NAME + '.' + COLUMN_NAME + '='
                   + DATA_TYPE + CASE WHEN CHARACTER_MAXIMUM_LENGTH IS NULL THEN ''
                                      WHEN CHARACTER_MAXIMUM_LENGTH = -1 THEN '(max)'
                                      ELSE '(' + CAST(CHARACTER_MAXIMUM_LENGTH AS varchar(10)) + ')' END
                   + CASE IS_NULLABLE WHEN 'YES' THEN ' NULL' ELSE ' NOT NULL' END AS Value
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME IN ('Incidents', 'IncidentHistory')
            """).ToListAsync();

        return rows.Select(r => r.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
    }

    private static Task<List<string>> ReadIndexesAsync(AppDbContext db) =>
        db.Database.SqlQueryRaw<string>("""
            SELECT i.name + '|' + CASE i.is_unique WHEN 1 THEN 'True' ELSE 'False' END
                   + '|' + ISNULL((SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
                                   FROM sys.index_columns ic
                                   JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                                   WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0), '')
                   + '|' + ISNULL((SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY c.name)
                                   FROM sys.index_columns ic
                                   JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                                   WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1), '')
                   + '|' + ISNULL(i.filter_definition, '') AS Value
            FROM sys.indexes i
            WHERE i.name IS NOT NULL AND OBJECT_NAME(i.object_id) IN ('Incidents', 'IncidentHistory')
            """).ToListAsync();
}
