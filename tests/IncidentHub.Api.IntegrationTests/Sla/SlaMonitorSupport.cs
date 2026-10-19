using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Sla.EvaluateSla;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace IncidentHub.Api.IntegrationTests.Sla;

/// <summary>
/// Seeds incidents with chosen SLA times and reads back what the SLA monitor did. The handler works on the whole database, so
/// every test seeds due times relative to the real clock and asserts only on its own incident ids.
/// </summary>
internal sealed class SlaMonitorSupport(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    public async Task<Guid> SeedAsync(
        DateTimeOffset warnAt,
        DateTimeOffset dueAt,
        Action<Incident, Actor, DateTimeOffset>? move = null)
    {
        var appId = await factory.CreateAppAsync();
        var (id, _) = await _seeder.AddIncidentAsync(
            appId,
            $"SLA monitor {Guid.NewGuid():N}",
            configure: (incident, actor, now) =>
            {
                incident.SetSlaDueTimes(now.AddMinutes(15), dueAt, warnAt);
                move?.Invoke(incident, actor, now);
            });
        return id;
    }

    public static void ToResolved(Incident incident, Actor actor, DateTimeOffset now)
    {
        incident.TransitionTo(IncidentStatus.Triaged, actor, null, now);
        incident.TransitionTo(IncidentStatus.InProgress, actor, null, now);
        incident.TransitionTo(IncidentStatus.Resolved, actor, "Fixed", now);
    }

    public static void ToClosed(Incident incident, Actor actor, DateTimeOffset now) =>
        incident.TransitionTo(IncidentStatus.Closed, actor, null, now, CloseReason.Invalid);

    public static void ToOnHold(Incident incident, Actor actor, DateTimeOffset now)
    {
        incident.TransitionTo(IncidentStatus.Triaged, actor, null, now);
        incident.TransitionTo(IncidentStatus.OnHold, actor, null, now);
    }

    public async Task<EvaluateSlaResult> RunHandlerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await NewHandler(db).Handle(new EvaluateSlaCommand(), CancellationToken.None);
    }

    public static EvaluateSlaHandler NewHandler(IncidentHub.Application.Abstractions.IAppDbContext db) =>
        new(db, new FakeTimeProvider(DateTimeOffset.UtcNow), NullLogger<EvaluateSlaHandler>.Instance);

    public async Task<IncidentState> StateAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var incident = await db.Incidents.AsNoTracking().SingleAsync(i => i.Id == id);
        var idText = id.ToString();
        var breachHistory = await db.IncidentHistory.AsNoTracking()
            .Where(h => h.IncidentId == id && h.Field == nameof(Incident.IsSlaBreached))
            .ToListAsync();

        return new IncidentState(
            incident.IsSlaBreached,
            incident.SlaWarnAt,
            incident.SlaWarnedAt,
            breachHistory.Count,
            breachHistory.Count(h => h.ActorId is null),
            await db.OutboxMessages.CountAsync(m => m.Type == "SlaWarning" && m.Payload.Contains(idText)),
            await db.OutboxMessages.CountAsync(m => m.Type == "SlaBreached" && m.Payload.Contains(idText)));
    }

    internal sealed record IncidentState(
        bool IsSlaBreached,
        DateTimeOffset? SlaWarnAt,
        DateTimeOffset? SlaWarnedAt,
        int BreachHistoryRows,
        int SystemBreachHistoryRows,
        int WarningRows,
        int BreachRows);
}
