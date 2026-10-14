using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Outbox;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Outbox;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OutboxPersistenceTests(ApiFactory factory)
{
    private const string PreviousMigration = "20261011064809_AddIncidents";
    private const string PreviousToNextAttemptAt = "20261013112822_AttachmentLinkConcurrency";

    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    // ---- Outbox rows written with the change ----

    [Fact]
    public async Task SaveIncident_Reported_WritesOutboxRowInSameSave()
    {
        var seed = await SeedAsync();
        var incident = NewIncident(seed, Unique("reported"));
        var raised = incident.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<IncidentReported>().Subject;

        await SaveNewAsync(incident);

        await using var readDb = NewContext();
        var rows = await RowsForAsync(readDb, incident.Id);
        var row = rows.Should().ContainSingle().Subject;
        row.Type.Should().Be(nameof(IncidentReported));
        row.ProcessedAt.Should().BeNull();
        row.Attempts.Should().Be(0);
        row.Error.Should().BeNull();
        row.OccurredAt.Should().Be(T0);
        JsonSerializer.Deserialize<IncidentReported>(row.Payload, OutboxJson.Options).Should().Be(raised);
    }

    [Fact]
    public async Task SaveIncident_SaveFails_RollsBackIncidentHistoryAndOutbox()
    {
        var seed = await SeedAsync();
        var title = Unique("rollback");
        var first = NewIncident(seed, title);
        await SaveNewAsync(first);

        var second = NewIncident(seed, title.ToUpperInvariant());
        await using var secondDb = NewContext();
        secondDb.Incidents.Add(second);
        var save = () => secondDb.SaveChangesAsync();

        await save.Should().ThrowAsync<DuplicateIncidentException>();

        await using var readDb = NewContext();
        (await readDb.Incidents.AnyAsync(i => i.Id == second.Id)).Should().BeFalse();
        (await readDb.IncidentHistory.AnyAsync(h => h.IncidentId == second.Id)).Should().BeFalse();
        (await RowsForAsync(readDb, second.Id)).Should().BeEmpty();
        (await RowsForAsync(readDb, first.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task SaveIncident_Success_ClearsDomainEvents()
    {
        var seed = await SeedAsync();
        var incident = NewIncident(seed, Unique("cleared"));

        await SaveNewAsync(incident);

        incident.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveIncident_Failure_KeepsDomainEventsAndDetachesOutbox()
    {
        var seed = await SeedAsync();
        var title = Unique("kept");
        await SaveNewAsync(NewIncident(seed, title));

        var duplicate = NewIncident(seed, title);
        await using var secondDb = NewContext();
        secondDb.Incidents.Add(duplicate);

        var save = () => secondDb.SaveChangesAsync();
        await save.Should().ThrowAsync<DuplicateIncidentException>();

        duplicate.DomainEvents.Should().ContainSingle();
        secondDb.ChangeTracker.Entries<OutboxMessage>().Should().BeEmpty();
    }

    [Fact]
    public async Task SaveIncident_RetryAfterFailure_WritesEachEventOnce()
    {
        var seed = await SeedAsync();
        var title = Unique("retry");
        var blocker = NewIncident(seed, title);
        await SaveNewAsync(blocker);

        var incident = NewIncident(seed, title);
        await using var retryDb = NewContext();
        retryDb.Incidents.Add(incident);
        var firstTry = () => retryDb.SaveChangesAsync();
        await firstTry.Should().ThrowAsync<DuplicateIncidentException>();

        // Close the blocking incident in another context, then retry the very same save.
        await using (var db = NewContext())
        {
            var tracked = await db.Incidents.SingleAsync(i => i.Id == blocker.Id);
            tracked.TransitionTo(IncidentStatus.Closed, seed.Responder, "Invalid report", T0.AddMinutes(1), CloseReason.Invalid);
            await db.SaveChangesAsync();
        }

        await retryDb.SaveChangesAsync();

        await using var readDb = NewContext();
        (await RowsForAsync(readDb, incident.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task SaveIncidentSync_Reported_WritesOutboxRowAndClearsEvents()
    {
        var seed = await SeedAsync();
        var incident = NewIncident(seed, Unique("sync"));
        await using (var db = NewContext())
        {
            db.Incidents.Add(incident);
            db.SaveChanges();
        }

        incident.DomainEvents.Should().BeEmpty();
        await using var readDb = NewContext();
        (await RowsForAsync(readDb, incident.Id)).Should().ContainSingle().Which.Type.Should().Be(nameof(IncidentReported));
    }

    [Fact]
    public async Task SaveIncidentSync_SameFingerprintBothOpen_ThrowsDuplicateIncidentAndDetachesOutbox()
    {
        var seed = await SeedAsync();
        var title = Unique("sync-dup");
        await SaveNewAsync(NewIncident(seed, title));

        var duplicate = NewIncident(seed, title);
        await using var db = NewContext();
        db.Incidents.Add(duplicate);
        var save = () => db.SaveChanges();

        save.Should().Throw<DuplicateIncidentException>();
        duplicate.DomainEvents.Should().ContainSingle();
        db.ChangeTracker.Entries<OutboxMessage>().Should().BeEmpty();
    }

    [Fact]
    public async Task TransitionIncident_Persisted_WritesIncidentTransitionedRow()
    {
        var seed = await SeedAsync();
        var incident = NewIncident(seed, Unique("transition"));
        await SaveNewAsync(incident);

        await using (var db = NewContext())
        {
            var tracked = await db.Incidents.SingleAsync(i => i.Id == incident.Id);
            tracked.TransitionTo(IncidentStatus.Triaged, seed.Responder, null, T0.AddMinutes(5));
            await db.SaveChangesAsync();
        }

        await using var readDb = NewContext();
        var rows = await RowsForAsync(readDb, incident.Id);
        rows.Select(r => r.Type).Should().BeEquivalentTo(new[] { nameof(IncidentReported), nameof(IncidentTransitioned) });
        var transitioned = JsonSerializer.Deserialize<IncidentTransitioned>(
            rows.Single(r => r.Type == nameof(IncidentTransitioned)).Payload, OutboxJson.Options)!;
        transitioned.IncidentId.Should().Be(incident.Id);
        transitioned.From.Should().Be(IncidentStatus.New);
        transitioned.To.Should().Be(IncidentStatus.Triaged);
    }

    [Fact]
    public async Task SaveIncident_AssignAndUpdate_WritesOneRowPerEvent()
    {
        var seed = await SeedAsync();
        var incident = NewIncident(seed, Unique("assign"));
        await SaveNewAsync(incident);

        await using (var db = NewContext())
        {
            var tracked = await db.Incidents.SingleAsync(i => i.Id == incident.Id);
            tracked.Assign(seed.ResponderId, null, seed.Responder, T0.AddMinutes(2));
            tracked.Update(new IncidentChanges { Title = Unique("renamed") }, seed.Responder, T0.AddMinutes(3));
            await db.SaveChangesAsync();
        }

        await using var readDb = NewContext();
        var rows = await RowsForAsync(readDb, incident.Id);
        rows.Select(r => r.Type).Should().BeEquivalentTo(
            new[] { nameof(IncidentReported), nameof(IncidentAssigned), nameof(IncidentUpdated) });
    }

    // ---- Migration and schema ----

    [Fact]
    public async Task Migrate_AddOutboxMessages_UpAndDown()
    {
        await using var db = NewIsolatedContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);
        (await ScalarAsync(db, "SELECT COUNT(*) FROM sys.tables WHERE name = 'OutboxMessages'")).Should().Be(0);

        await db.Database.MigrateAsync();
        (await ScalarAsync(db, "SELECT COUNT(*) FROM sys.tables WHERE name = 'OutboxMessages'")).Should().Be(1);

        await migrator.MigrateAsync(PreviousMigration);
        (await ScalarAsync(db, "SELECT COUNT(*) FROM sys.tables WHERE name = 'OutboxMessages'")).Should().Be(0);
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Migrate_AddOutboxNextAttemptAt_UpAndDown()
    {
        const string ColumnCount = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OutboxMessages') AND name = 'NextAttemptAt'";
        await using var db = NewIsolatedContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousToNextAttemptAt);
        (await ScalarAsync(db, ColumnCount)).Should().Be(0);

        await db.Database.MigrateAsync();
        (await ScalarAsync(db, ColumnCount)).Should().Be(1);

        await migrator.MigrateAsync(PreviousToNextAttemptAt);
        (await ScalarAsync(db, ColumnCount)).Should().Be(0);
        (await ScalarAsync(db, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_OutboxMessages_Unprocessed'")).Should().Be(1);
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Migrate_EmptyDatabase_IncludesOutboxMessages()
    {
        await using var db = NewIsolatedContext();

        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await ScalarAsync(db, "SELECT COUNT(*) FROM sys.tables WHERE name = 'OutboxMessages'")).Should().Be(1);
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Schema_OutboxMessages_MatchesDatabaseDoc()
    {
        await using var db = NewContext();

        var columns = (await db.Database.SqlQueryRaw<string>("""
            SELECT COLUMN_NAME + '='
                   + DATA_TYPE + CASE WHEN CHARACTER_MAXIMUM_LENGTH IS NULL THEN ''
                                      WHEN CHARACTER_MAXIMUM_LENGTH = -1 THEN '(max)'
                                      ELSE '(' + CAST(CHARACTER_MAXIMUM_LENGTH AS varchar(10)) + ')' END
                   + CASE IS_NULLABLE WHEN 'YES' THEN ' NULL' ELSE ' NOT NULL' END AS Value
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'OutboxMessages'
            """).ToListAsync()).Select(r => r.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);

        columns["Id"].Should().Be("uniqueidentifier NOT NULL");
        columns["Type"].Should().Be("nvarchar(50) NOT NULL");
        columns["Payload"].Should().Be("nvarchar(max) NOT NULL");
        columns["OccurredAt"].Should().Be("datetimeoffset NOT NULL");
        columns["ProcessedAt"].Should().Be("datetimeoffset NULL");
        columns["Attempts"].Should().Be("int NOT NULL");
        columns["Error"].Should().Be("nvarchar(max) NULL");
        columns["NextAttemptAt"].Should().Be("datetimeoffset NULL");

        (await ScalarAsync(db, """
            SELECT COUNT(*) FROM sys.default_constraints dc
            JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
            WHERE dc.parent_object_id = OBJECT_ID('dbo.OutboxMessages') AND c.name = 'Attempts' AND dc.definition = '((0))'
            """)).Should().Be(1);

        var index = await db.Database.SqlQueryRaw<string>("""
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
            WHERE i.name = 'IX_OutboxMessages_Unprocessed'
            """).SingleAsync();

        index.Should().StartWith("IX_OutboxMessages_Unprocessed|False|ProcessedAt|Attempts,NextAttemptAt,OccurredAt|");
        index.Should().Contain("[ProcessedAt] IS NULL");
    }

    // ---- Helpers ----

    private sealed record Seed(Guid AppId, Actor Reporter, Guid ResponderId, Actor Responder);

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

    private async Task SaveNewAsync(Incident incident)
    {
        await using var db = NewContext();
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();
    }

    private async Task<Seed> SeedAsync()
    {
        var appId = await factory.CreateAppAsync();
        var reporterId = await factory.EnsureUserAsync(Guid.NewGuid().ToString());
        var responderId = await factory.EnsureUserAsync(Guid.NewGuid().ToString());
        return new Seed(
            appId,
            new Actor(reporterId, new HashSet<UserRole> { UserRole.Reporter }),
            responderId,
            new Actor(responderId, new HashSet<UserRole> { UserRole.Responder }));
    }

    /// <summary>Outbox rows whose payload mentions the incident id, oldest first.</summary>
    private static async Task<List<OutboxMessage>> RowsForAsync(AppDbContext db, Guid incidentId)
    {
        var id = incidentId.ToString();
        return await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Payload.Contains(id))
            .OrderBy(m => m.OccurredAt)
            .ToListAsync();
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Sql"])
            .Options);

    private AppDbContext NewIsolatedContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(factory.NewDatabaseConnectionString()).Options);

    private static Task<int> ScalarAsync(AppDbContext db, string sql) =>
        db.Database.SqlQueryRaw<int>("SELECT (" + sql + ") AS Value").SingleAsync();
}
