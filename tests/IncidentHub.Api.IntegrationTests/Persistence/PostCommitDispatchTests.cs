using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IncidentHub.Api.IntegrationTests.Persistence;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostCommitDispatchTests(ApiFactory factory)
{
    private readonly RecordingDispatcher _dispatcher = new();

    [Fact]
    public async Task SaveChanges_Success_DispatchesThenClearsEvents()
    {
        var incident = await NewIncidentAsync($"Checkout fails {Guid.NewGuid():N}");
        await using var db = NewContext();
        db.Incidents.Add(incident);
        incident.DomainEvents.Should().ContainSingle();

        await db.SaveChangesAsync();

        _dispatcher.Batches.Should().ContainSingle().Which.Should().ContainSingle().Which.Should().BeOfType<IncidentReported>();
        incident.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveChanges_Failure_KeepsEventsAndDispatchesNothing()
    {
        var title = $"Checkout fails {Guid.NewGuid():N}";
        var appId = await factory.CreateAppAsync();
        await using (var first = NewContext())
        {
            first.Incidents.Add(await NewIncidentAsync(title, appId));
            await first.SaveChangesAsync();
        }

        _dispatcher.Batches.Clear();
        var duplicate = await NewIncidentAsync(title, appId);
        await using var db = NewContext();
        db.Incidents.Add(duplicate);

        var save = () => db.SaveChangesAsync();

        await save.Should().ThrowAsync<DuplicateIncidentException>();
        _dispatcher.Batches.Should().BeEmpty();
        duplicate.DomainEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task SaveChanges_DispatcherThrows_WriteStillCommits()
    {
        var incident = await NewIncidentAsync($"Checkout fails {Guid.NewGuid():N}");
        _dispatcher.Throw = true;
        await using (var db = NewContext())
        {
            db.Incidents.Add(incident);
            await db.SaveChangesAsync();
        }

        await using var check = NewContext();
        (await check.Incidents.AsNoTracking().AnyAsync(i => i.Id == incident.Id)).Should().BeTrue();
        incident.DomainEvents.Should().BeEmpty();
    }

    private async Task<Incident> NewIncidentAsync(string title, Guid? applicationId = null)
    {
        var appId = applicationId ?? await factory.CreateAppAsync();
        var reporter = await factory.EnsureUserAsync(Guid.NewGuid().ToString());
        var actor = new Actor(reporter, new HashSet<UserRole> { UserRole.Responder });
        return Incident.Report(
            appId, AppEnvironment.Production, Severity.High, title, "<p>Users see an error page.</p>", null, IncidentSource.Manual, actor, DateTimeOffset.UtcNow);
    }

    private AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:Sql"])
            .Options;
        return new AppDbContext(options, new PostCommitHook(_dispatcher, TimeProvider.System, NullLogger<PostCommitHook>.Instance));
    }

    private sealed class RecordingDispatcher : IPostCommitDispatcher
    {
        public List<IReadOnlyList<IDomainEvent>> Batches { get; } = [];

        public bool Throw { get; set; }

        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
        {
            Batches.Add(events);
            return Throw ? throw new InvalidOperationException("hub down") : Task.CompletedTask;
        }
    }
}
