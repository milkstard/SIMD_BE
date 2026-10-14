using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Api.IntegrationTests.Notifications;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Outbox;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Teams;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Notifications;
using IncidentHub.Infrastructure.Outbox;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace IncidentHub.Api.IntegrationTests.Outbox;

/// <summary>
/// The email triggers end to end: real domain changes write outbox rows, the real runner dispatches them through the real handlers and
/// <c>EmailSender</c> to Mailpit. Runs on its own migrated database (the runner claims rows globally) with a fake clock for the runner.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EmailDispatchTests(ApiFactory factory, MailpitFixture mailpit) : IAsyncLifetime, IClassFixture<MailpitFixture>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Before = T0.AddMinutes(-1);

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _connectionString;

    private readonly FakeTimeProvider _time = new(T0);

    public async Task InitializeAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_connectionString is null)
            {
                var connectionString = factory.NewDatabaseConnectionString();
                await using var migrating = NewContext(connectionString);
                await migrating.Database.MigrateAsync();
                _connectionString = connectionString;
            }
        }
        finally
        {
            Gate.Release();
        }

        await using var db = NewContext();
        await db.OutboxMessages.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- Reported ----

    [Fact]
    public async Task Report_ThenDispatch_EmailsOwningTeamOnce()
    {
        var seed = await SeedIncidentAsync("Checkout <b>fails</b>");
        await using var worker = Worker();

        (await worker.RunAsync()).Should().Be(1);
        (await worker.RunAsync()).Should().Be(0, "a processed message is not claimed again");

        var mails = await mailpit.ListAsync(seed.Token);
        mails.Should().ContainSingle().Which.To.Should().Be(seed.TeamEmail);
        mails[0].Subject.Should().StartWith("[INC-").And.Contain("High incident reported");

        using var message = await mailpit.WaitForMessageAsync(seed.Token);
        var html = message.RootElement.GetProperty("HTML").GetString()!;
        html.Should().NotContain("<b>fails</b>").And.Contain("&lt;b&gt;fails");
        message.RootElement.GetProperty("Text").GetString().Should().Contain("https://app.example.com/incidents/INC-");
        (await LoadOutboxAsync("IncidentReported")).Should().OnlyContain(m => m.ProcessedAt != null && m.Error == null);
    }

    // ---- Assigned ----

    [Fact]
    public async Task Assign_ThenDispatch_EmailsAssignee()
    {
        var seed = await SeedIncidentAsync();
        await ChangeAsync(seed, i => i.Assign(seed.AssigneeId, null, seed.Responder, Before));
        await using var worker = Worker();

        await worker.RunAsync();

        var assigned = (await mailpit.ListAsync(seed.Token)).Where(m => m.Subject.Contains("Assigned to you")).ToList();
        assigned.Should().ContainSingle().Which.To.Should().Be(seed.AssigneeEmail);
    }

    [Fact]
    public async Task Assign_SameUserAgain_NoSecondMail()
    {
        var seed = await SeedIncidentAsync();
        await ChangeAsync(seed, i => i.Assign(seed.AssigneeId, null, seed.Responder, Before));
        await using var worker = Worker();
        await worker.RunAsync();

        // Only the team changes: the assignee is the same, so no new mail.
        await ChangeAsync(seed, i => i.Assign(seed.AssigneeId, seed.TeamId, seed.Responder, Before));
        await worker.RunAsync();

        (await mailpit.ListAsync(seed.Token)).Count(m => m.Subject.Contains("Assigned to you")).Should().Be(1);
        (await LoadOutboxAsync("IncidentAssigned")).Should().HaveCount(2).And.OnlyContain(m => m.ProcessedAt != null);
    }

    // ---- Resolved ----

    [Fact]
    public async Task Resolve_ThenDispatch_EmailsReporter()
    {
        var seed = await SeedIncidentAsync();
        await ChangeAsync(seed, i => i.TransitionTo(IncidentStatus.Triaged, seed.Responder, null, Before));
        await ChangeAsync(seed, i => i.TransitionTo(IncidentStatus.InProgress, seed.Responder, null, Before));
        await ChangeAsync(seed, i => i.TransitionTo(IncidentStatus.Resolved, seed.Responder, "Gateway timeout raised <b>30 s</b>", Before));
        await using var worker = Worker();

        await worker.RunAsync();

        var resolved = (await mailpit.ListAsync(seed.Token)).Where(m => m.Subject.Contains("Resolved:")).ToList();
        resolved.Should().ContainSingle().Which.To.Should().Be(seed.ReporterEmail);
        using var message = await mailpit.WaitForMessageAsync($"Resolved: Checkout fails {seed.Token}");
        message.RootElement.GetProperty("HTML").GetString().Should().Contain("Gateway timeout raised &lt;b&gt;30 s");
    }

    [Fact]
    public async Task Transition_ToTriaged_NoMail()
    {
        var seed = await SeedIncidentAsync();
        await ChangeAsync(seed, i => i.TransitionTo(IncidentStatus.Triaged, seed.Responder, null, Before));
        await using var worker = Worker();

        await worker.RunAsync();

        (await mailpit.ListAsync(seed.Token)).Should().ContainSingle("only the reported mail is sent");
        (await LoadOutboxAsync("IncidentTransitioned")).Should().ContainSingle().Which.ProcessedAt.Should().NotBeNull();
    }

    // ---- Other types, failures, concurrency ----

    [Fact]
    public async Task Dispatch_CommentAdded_MarkedProcessedWithoutMail()
    {
        var seed = await SeedIncidentAsync();
        await using (var db = NewContext())
        {
            await db.OutboxMessages.ExecuteDeleteAsync();
        }

        await ChangeAsync(seed, i => i.AddComment("Looking into it", isInternal: false, seed.Reporter, Before));
        await using var worker = Worker();

        await worker.RunAsync();

        (await mailpit.ListAsync(seed.Token)).Should().BeEmpty();
        var row = (await LoadOutboxAsync("CommentAdded")).Should().ContainSingle().Subject;
        row.ProcessedAt.Should().NotBeNull();
        row.Error.Should().Be(OutboxDispatchRunner.NoHandlerNote);
    }

    [Fact]
    public async Task Dispatch_SmtpDown_KeepsMessageAndRetriesAfterRecovery()
    {
        var seed = await SeedIncidentAsync();

        await using (var down = Worker(smtpHost: "127.0.0.1", smtpPort: FreePort()))
        {
            await down.RunAsync();
        }

        var failed = (await LoadOutboxAsync("IncidentReported")).Should().ContainSingle().Subject;
        failed.ProcessedAt.Should().BeNull();
        failed.Attempts.Should().Be(1);
        failed.NextAttemptAt.Should().NotBeNull();
        (await mailpit.ListAsync(seed.Token)).Should().BeEmpty();

        _time.Advance(TimeSpan.FromMinutes(2));
        await using var recovered = Worker();
        (await recovered.RunAsync()).Should().Be(1);

        (await mailpit.ListAsync(seed.Token)).Should().ContainSingle();
        (await LoadOutboxAsync("IncidentReported")).Should().ContainSingle().Which.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Dispatch_TwoRunners_OneMailPerMessage()
    {
        var seeds = new List<Seeded>();
        for (var i = 0; i < 4; i++)
        {
            seeds.Add(await SeedIncidentAsync());
        }

        await using var first = Worker();
        await using var second = Worker();
        await Task.WhenAll(first.RunAsync(), second.RunAsync());
        await first.RunAsync();

        foreach (var seed in seeds)
        {
            (await mailpit.ListAsync(seed.Token)).Should().ContainSingle();
        }

        (await LoadOutboxAsync("IncidentReported")).Should().HaveCount(4).And.OnlyContain(m => m.ProcessedAt != null);
    }

    [Fact]
    public void Api_DoesNotRegisterEmailHandlersOrSender()
    {
        var services = factory.Services;

        services.GetKeyedServices<IOutboxMessageHandler>(KeyedService.AnyKey).Should().BeEmpty();
        services.GetService<INotificationSender>().Should().BeNull();
        services.GetServices<IHostedService>().Select(s => s.GetType().Name).Should().NotContain("OutboxDispatcher");
    }

    // ---- Helpers ----

    private static AppDbContext NewContext(string? connectionString = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString ?? _connectionString).Options);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private WorkerHarness Worker(string? smtpHost = null, int? smtpPort = null) =>
        new(_connectionString!, smtpHost ?? mailpit.SmtpHost, smtpPort ?? mailpit.SmtpPort, _time);

    private static async Task<List<OutboxMessage>> LoadOutboxAsync(string type)
    {
        await using var db = NewContext();
        return await db.OutboxMessages.AsNoTracking().Where(m => m.Type == type).OrderBy(m => m.OccurredAt).ToListAsync();
    }

    private static async Task ChangeAsync(Seeded seed, Action<Incident> change)
    {
        await using var db = NewContext();
        var incident = await db.Incidents.SingleAsync(i => i.Id == seed.IncidentId);
        change(incident);
        await db.SaveChangesAsync();
    }

    /// <summary>A team, three users (reporter, responder, assignee), an application and one reported incident with a unique title token.</summary>
    private static async Task<Seeded> SeedIncidentAsync(string titlePrefix = "Checkout fails")
    {
        var token = Guid.NewGuid().ToString("N");
        await using var db = NewContext();

        var team = Team.Create($"Team {token}", $"team-{token}@example.com", null);
        var reporter = User.Create($"rep-{token}", "Rita Reporter", $"reporter-{token}@example.com", Before);
        var responder = User.Create($"res-{token}", "Rob Responder", $"responder-{token}@example.com", Before);
        var assignee = User.Create($"ass-{token}", "Ana Assignee", $"assignee-{token}@example.com", Before);
        db.Teams.Add(team);
        db.Users.AddRange(reporter, responder, assignee);
        await db.SaveChangesAsync();

        var app = MonitoredApp.Create($"App {token}", $"A{token[..12]}", team.Id, null, [AppEnvironment.Production]);
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        var reporterActor = new Actor(reporter.Id, new HashSet<UserRole> { UserRole.Reporter });
        var responderActor = new Actor(responder.Id, new HashSet<UserRole> { UserRole.Responder });
        var incident = Incident.Report(
            app.Id,
            AppEnvironment.Production,
            Severity.High,
            $"{titlePrefix} {token}",
            "<p>Error page after paying.</p>",
            null,
            IncidentSource.Manual,
            reporterActor,
            Before);
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        return new Seeded(
            incident.Id, token, team.Id, $"team-{token}@example.com", $"reporter-{token}@example.com", $"assignee-{token}@example.com",
            assignee.Id, reporterActor, responderActor);
    }

    private sealed record Seeded(
        Guid IncidentId,
        string Token,
        Guid TeamId,
        string TeamEmail,
        string ReporterEmail,
        string AssigneeEmail,
        Guid AssigneeId,
        Actor Reporter,
        Actor Responder);

    /// <summary>The Worker's wiring in miniature: its own service provider, the real senders pointed at an SMTP host, a runner on a fake clock.</summary>
    private sealed class WorkerHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly FakeTimeProvider _time;

        public WorkerHarness(string connectionString, string smtpHost, int smtpPort, FakeTimeProvider time)
        {
            _time = time;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Email:Enabled"] = "true",
                ["Notifications:Email:Host"] = smtpHost,
                ["Notifications:Email:Port"] = smtpPort.ToString(),
                ["Notifications:Email:FromAddress"] = "noreply@incidenthub.local",
                ["Notifications:Email:FromName"] = "IncidentHub",
                ["Notifications:Email:PublicBaseUrl"] = "https://app.example.com",
                ["Notifications:Resilience:MaxRetries"] = "1",
                ["Notifications:Resilience:RetryBaseDelay"] = "00:00:00.010",
                ["Notifications:Resilience:AttemptTimeout"] = "00:00:05",
                ["Notifications:Resilience:TotalTimeout"] = "00:00:20",
            }).Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
            services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
            services.AddNotificationSenders(configuration);
            _provider = services.BuildServiceProvider();
        }

        public async Task<int> RunAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            var runner = new OutboxDispatchRunner(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                new OutboxEventTypes(),
                _provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new OutboxDispatcherOptions()),
                _time,
                NullLogger<OutboxDispatchRunner>.Instance);
            return await runner.RunOnceAsync(CancellationToken.None);
        }

        public ValueTask DisposeAsync() => _provider.DisposeAsync();
    }
}
