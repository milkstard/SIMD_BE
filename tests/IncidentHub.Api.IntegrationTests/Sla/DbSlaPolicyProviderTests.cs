using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Sla;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;
using IncidentHub.Infrastructure.Persistence;
using IncidentHub.Infrastructure.Sla;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IncidentHub.Api.IntegrationTests.Sla;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DbSlaPolicyProviderTests(ApiFactory factory)
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task GetAsync_SeededRow_ReturnsTheStoredPolicy()
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISlaPolicyProvider>();

        var policy = await provider.GetAsync(Severity.Critical, CancellationToken.None);

        policy.Should().Be(new SlaPolicy(Severity.Critical, 15, 240, 80));
    }

    [Fact]
    public async Task GetAsync_MissingRow_FallsBackToDefaultsAndLogsWarning()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var defaults = new SlaDefaultsOptions();
        var logger = new CapturingLogger<DbSlaPolicyProvider>();
        var provider = new DbSlaPolicyProvider(db, Options.Create(defaults), logger);

        var row = await db.SlaPolicies.SingleAsync(p => p.Severity == Severity.Low);
        db.SlaPolicies.Remove(row);
        await db.SaveChangesAsync();
        try
        {
            var policy = await provider.GetAsync(Severity.Low, CancellationToken.None);

            policy.Should().Be(defaults.ToPolicy(Severity.Low));
            logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("Low"));
        }
        finally
        {
            db.SlaPolicies.Add(SlaPolicySetting.Create(Severity.Low, row.AckMinutes, row.ResolveMinutes, row.EscalateAtPercent));
            await db.SaveChangesAsync();
        }
    }
}
