using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Common.Sla;
using IncidentHub.Application.Sla.UpdateSlaPolicy;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IncidentHub.Api.IntegrationTests.Sla;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SlaMonitorTests(ApiFactory factory)
{
    private readonly SlaMonitorSupport _sla = new(factory);

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    [Fact]
    public async Task Monitor_ManyTicks_ExactlyOneWarningRow()
    {
        var id = await _sla.SeedAsync(Now.AddMinutes(-10), Now.AddHours(1));

        for (var tick = 0; tick < 4; tick++)
        {
            await _sla.RunHandlerAsync();
        }

        (await _sla.StateAsync(id)).WarningRows.Should().Be(1);
    }

    [Fact]
    public async Task Monitor_Breach_SetsFlagHistoryAndOutboxInOneSave()
    {
        var id = await _sla.SeedAsync(Now.AddHours(-2), Now.AddMinutes(-1));

        await _sla.RunHandlerAsync();

        var state = await _sla.StateAsync(id);
        state.IsSlaBreached.Should().BeTrue();
        state.SystemBreachHistoryRows.Should().Be(1);
        state.BreachRows.Should().Be(1);
    }

    [Fact]
    public async Task Monitor_PolicyChange_DoesNotMoveExistingWarnAt()
    {
        try
        {
            var incident = await ReportAsync("High");
            var id = incident.GetProperty("id").GetGuid();
            var createdAt = incident.GetProperty("createdAt").GetDateTimeOffset();
            var before = (await _sla.StateAsync(id)).SlaWarnAt;

            // The default High policy is 480 minutes at 80 percent.
            before.Should().Be(createdAt.AddMinutes(384));

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var rowVersion = (await db.SlaPolicies.AsNoTracking().SingleAsync(p => p.Severity == Severity.High)).RowVersion;
                var handler = new UpdateSlaPolicyHandler(db, NullLogger<UpdateSlaPolicyHandler>.Instance);
                await handler.Handle(new UpdateSlaPolicyCommand("High", 5, 10, 50, rowVersion), CancellationToken.None);
            }

            (await _sla.StateAsync(id)).SlaWarnAt.Should().Be(before);
        }
        finally
        {
            await RestoreDefaultsAsync();
        }
    }

    [Fact]
    public async Task Migration_AddIncidentSlaWarn_CreatesColumnsAndFilteredIndex()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var columns = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Incidents') AND name IN ('SlaWarnAt', 'SlaWarnedAt')")
            .SingleAsync();
        var index = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'IX_Incidents_SlaCandidates' AND has_filter = 1")
            .SingleAsync();

        columns.Should().Be(2);
        index.Should().Be(1);
    }

    private async Task<JsonElement> ReportAsync(string severity)
    {
        var appId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var client = factory.CreateClient().AsUser(factory, oid, UserRole.Responder);

        var response = await client.PostAsJsonAsync("/api/v1/incidents", new
        {
            applicationId = appId,
            environment = "Production",
            severity,
            title = $"Checkout fails {Guid.NewGuid():N}",
            description = "<p>Error page after paying.</p>",
            stepsToReproduce = "1. Pay",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task RestoreDefaultsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var defaults = new SlaDefaultsOptions();
        foreach (var setting in await db.SlaPolicies.ToListAsync())
        {
            var target = defaults.For(setting.Severity);
            setting.Update(target.AckMinutes, target.ResolveMinutes, target.EscalateAtPercent);
        }

        await db.SaveChangesAsync();
    }
}
