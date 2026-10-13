using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Incidents.CanWatchIncident;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Incidents;

/// <summary>Handler against the real SQL container and the real team reader.</summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CanWatchIncidentHandlerTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    [Fact]
    public async Task Handle_TeamMember_ReturnsTrue()
    {
        var appId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var (incidentId, _) = await _seeder.AddIncidentAsync(appId, "Visible to the team");

        var result = await HandleAsync(incidentId, await factory.EnsureUserAsync(oid));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NonMember_ReturnsFalse()
    {
        var appId = await factory.CreateAppAsync();
        var (incidentId, _) = await _seeder.AddIncidentAsync(appId, "Other team's incident");

        var result = await HandleAsync(incidentId, await factory.EnsureUserAsync(Guid.NewGuid().ToString()));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UnknownIncident_ReturnsFalse()
    {
        var result = await HandleAsync(Guid.NewGuid(), await factory.EnsureUserAsync(Guid.NewGuid().ToString()));

        result.Should().BeFalse();
    }

    private async Task<bool> HandleAsync(Guid incidentId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var handler = new CanWatchIncidentHandler(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IAppTeamReader>());

        return await handler.Handle(new CanWatchIncidentQuery(incidentId, userId), CancellationToken.None);
    }
}
