using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Application.Common.Sla;
using IncidentHub.Application.Sla.UpdateSlaPolicy;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IncidentHub.Api.IntegrationTests.Sla;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class UpdateSlaPolicyHandlerTests(ApiFactory factory)
{
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

    private static async Task<byte[]> RowVersionAsync(AppDbContext db, string severity) =>
        (await db.SlaPolicies.AsNoTracking().SingleAsync(p => p.Severity == Enum.Parse<IncidentHub.Domain.Incidents.Severity>(severity))).RowVersion;

    [Fact]
    public async Task Handle_ValidCommand_UpdatesRow()
    {
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var handler = new UpdateSlaPolicyHandler(db, NullLogger<UpdateSlaPolicyHandler>.Instance);
            var rowVersion = await RowVersionAsync(db, "Medium");

            var result = await handler.Handle(new UpdateSlaPolicyCommand("medium", 20, 90, 60, rowVersion), CancellationToken.None);

            result.Id.Should().Be("Medium");
            result.AckMinutes.Should().Be(20);
            result.ResolveMinutes.Should().Be(90);
            result.EscalateAtPercent.Should().Be(60);
            using var verify = factory.Services.CreateScope();
            var stored = await verify.ServiceProvider.GetRequiredService<AppDbContext>().SlaPolicies.AsNoTracking()
                .SingleAsync(p => p.Severity == IncidentHub.Domain.Incidents.Severity.Medium);
            stored.AckMinutes.Should().Be(20);
            Convert.ToBase64String(stored.RowVersion).Should().Be(result.RowVersion);
        }
        finally
        {
            await RestoreDefaultsAsync();
        }
    }

    [Fact]
    public async Task Handle_StaleRowVersion_ThrowsConflictAndChangesNothing()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new UpdateSlaPolicyHandler(db, NullLogger<UpdateSlaPolicyHandler>.Instance);

        var act = () => handler.Handle(new UpdateSlaPolicyCommand("High", 20, 90, 60, [1, 2, 3]), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        using var verify = factory.Services.CreateScope();
        var stored = await verify.ServiceProvider.GetRequiredService<AppDbContext>().SlaPolicies.AsNoTracking()
            .SingleAsync(p => p.Severity == IncidentHub.Domain.Incidents.Severity.High);
        stored.AckMinutes.Should().Be(new SlaDefaultsOptions().High.AckMinutes);
    }

    [Theory]
    [InlineData("Bogus")]
    [InlineData("1")]
    public async Task Handle_UnknownId_ThrowsNotFound(string id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new UpdateSlaPolicyHandler(db, NullLogger<UpdateSlaPolicyHandler>.Instance);

        var act = () => handler.Handle(new UpdateSlaPolicyCommand(id, 20, 90, 60, [1]), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
