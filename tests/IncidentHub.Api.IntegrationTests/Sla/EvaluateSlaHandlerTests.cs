using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Sla.EvaluateSla;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;
using IncidentHub.Domain.Teams;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Sla;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EvaluateSlaHandlerTests(ApiFactory factory)
{
    private readonly SlaMonitorSupport _sla = new(factory);

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    [Fact]
    public async Task Evaluate_PastWarnAt_WarnsOnce()
    {
        var id = await _sla.SeedAsync(warnAt: Now.AddMinutes(-10), dueAt: Now.AddHours(1));

        var first = await _sla.RunHandlerAsync();
        await _sla.RunHandlerAsync();
        await _sla.RunHandlerAsync();

        first.Warned.Should().BeGreaterThanOrEqualTo(1);
        var state = await _sla.StateAsync(id);
        state.WarningRows.Should().Be(1);
        state.SlaWarnedAt.Should().NotBeNull();
        state.IsSlaBreached.Should().BeFalse();
        state.BreachRows.Should().Be(0);
    }

    [Fact]
    public async Task Evaluate_BeforeWarnAt_ChangesNothing()
    {
        var id = await _sla.SeedAsync(warnAt: Now.AddHours(1), dueAt: Now.AddHours(2));

        await _sla.RunHandlerAsync();

        var state = await _sla.StateAsync(id);
        state.WarningRows.Should().Be(0);
        state.SlaWarnedAt.Should().BeNull();
        state.IsSlaBreached.Should().BeFalse();
    }

    [Fact]
    public async Task Evaluate_PastDue_BreachesAndSkipsWarning()
    {
        var id = await _sla.SeedAsync(warnAt: Now.AddHours(-2), dueAt: Now.AddMinutes(-1));

        await _sla.RunHandlerAsync();
        await _sla.RunHandlerAsync();

        var state = await _sla.StateAsync(id);
        state.IsSlaBreached.Should().BeTrue();
        state.BreachHistoryRows.Should().Be(1);
        state.SystemBreachHistoryRows.Should().Be(1);
        state.BreachRows.Should().Be(1);
        state.WarningRows.Should().Be(0);
        state.SlaWarnedAt.Should().BeNull();
    }

    [Fact]
    public async Task Evaluate_WarnedThenPastDue_BreachesOnce()
    {
        var id = await _sla.SeedAsync(warnAt: Now.AddMinutes(-10), dueAt: Now.AddHours(1));
        await _sla.RunHandlerAsync();
        await ExecuteAsync($"UPDATE dbo.Incidents SET ResolveDueAt = DATEADD(minute, -1, SYSDATETIMEOFFSET()) WHERE Id = '{id}'");

        await _sla.RunHandlerAsync();
        await _sla.RunHandlerAsync();

        var state = await _sla.StateAsync(id);
        state.WarningRows.Should().Be(1);
        state.BreachRows.Should().Be(1);
        state.IsSlaBreached.Should().BeTrue();
    }

    [Fact]
    public async Task Evaluate_ResolvedIncident_Ignored()
    {
        var id = await _sla.SeedAsync(Now.AddHours(-2), Now.AddMinutes(-1), SlaMonitorSupport.ToResolved);

        await _sla.RunHandlerAsync();

        var state = await _sla.StateAsync(id);
        state.IsSlaBreached.Should().BeFalse();
        state.WarningRows.Should().Be(0);
        state.BreachRows.Should().Be(0);
    }

    [Fact]
    public async Task Evaluate_ClosedIncident_Ignored()
    {
        var id = await _sla.SeedAsync(Now.AddHours(-2), Now.AddMinutes(-1), SlaMonitorSupport.ToClosed);

        await _sla.RunHandlerAsync();

        var state = await _sla.StateAsync(id);
        state.IsSlaBreached.Should().BeFalse();
        state.WarningRows.Should().Be(0);
        state.BreachRows.Should().Be(0);
    }

    [Fact]
    public async Task Evaluate_OnHold_StillCounts()
    {
        var id = await _sla.SeedAsync(Now.AddHours(-2), Now.AddMinutes(-1), SlaMonitorSupport.ToOnHold);

        await _sla.RunHandlerAsync();

        (await _sla.StateAsync(id)).IsSlaBreached.Should().BeTrue();
    }

    [Fact]
    public async Task Evaluate_SeverityChangeAfterWarning_AllowsNewWarning()
    {
        var id = await _sla.SeedAsync(warnAt: Now.AddMinutes(-10), dueAt: Now.AddHours(1));
        await _sla.RunHandlerAsync();
        (await _sla.StateAsync(id)).WarningRows.Should().Be(1);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var incident = await db.Incidents.SingleAsync(i => i.Id == id);
            var actor = new Actor(incident.ReporterId, new HashSet<UserRole> { UserRole.Responder });
            var now = Now;
            incident.ChangeSeverity(Severity.Critical, "Full outage", now.AddMinutes(15), now.AddHours(2), now.AddMinutes(-1), actor, now);
            await db.SaveChangesAsync();
        }

        (await _sla.StateAsync(id)).SlaWarnedAt.Should().BeNull();
        await _sla.RunHandlerAsync();
        await _sla.RunHandlerAsync();

        (await _sla.StateAsync(id)).WarningRows.Should().Be(2);
    }

    [Fact]
    public async Task Evaluate_ConcurrencyConflict_SkipsAndContinues()
    {
        var conflicted = await _sla.SeedAsync(Now.AddHours(-3), Now.AddHours(-2));
        var other = await _sla.SeedAsync(Now.AddHours(-3), Now.AddHours(-1));
        EvaluateSlaResult result;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var handler = SlaMonitorSupport.NewHandler(new ConflictOnFirstSaveDb(db, BumpRowVersionAsync));
            result = await handler.Handle(new EvaluateSlaCommand(), CancellationToken.None);
        }

        result.Failed.Should().Be(1);
        (await _sla.StateAsync(conflicted)).IsSlaBreached.Should().BeFalse();
        (await _sla.StateAsync(other)).IsSlaBreached.Should().BeTrue();

        // The next pass picks the conflicted one up.
        await _sla.RunHandlerAsync();
        var retried = await _sla.StateAsync(conflicted);
        retried.IsSlaBreached.Should().BeTrue();
        retried.BreachRows.Should().Be(1);
    }

    [Fact]
    public async Task Evaluate_SeveralCandidates_HandlesAll()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(await _sla.SeedAsync(Now.AddHours(-3), Now.AddMinutes(-(i + 1))));
        }

        var result = await _sla.RunHandlerAsync();

        result.Breached.Should().BeGreaterThanOrEqualTo(3);
        foreach (var id in ids)
        {
            (await _sla.StateAsync(id)).IsSlaBreached.Should().BeTrue();
        }
    }

    private async Task BumpRowVersionAsync(Guid incidentId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Incidents SET Title = Title WHERE Id = {incidentId}");
    }

    private async Task ExecuteAsync(string sql)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>Changes the incident's row from another connection just before the first save, so that save loses the race.</summary>
    private sealed class ConflictOnFirstSaveDb(AppDbContext inner, Func<Guid, Task> bump) : IAppDbContext
    {
        private bool _bumped;

        public DbSet<User> Users => inner.Users;

        public DbSet<Team> Teams => inner.Teams;

        public DbSet<TeamMember> TeamMembers => inner.TeamMembers;

        public DbSet<MonitoredApp> Applications => inner.Applications;

        public DbSet<Incident> Incidents => inner.Incidents;

        public DbSet<IncidentHistory> IncidentHistory => inner.IncidentHistory;

        public DbSet<Comment> Comments => inner.Comments;

        public DbSet<Attachment> Attachments => inner.Attachments;

        public DbSet<SlaPolicySetting> SlaPolicies => inner.SlaPolicies;

        public ChangeTracker ChangeTracker => inner.ChangeTracker;

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (!_bumped)
            {
                _bumped = true;
                await bump(inner.ChangeTracker.Entries<Incident>().First().Entity.Id);
            }

            return await inner.SaveChangesAsync(cancellationToken);
        }
    }
}
