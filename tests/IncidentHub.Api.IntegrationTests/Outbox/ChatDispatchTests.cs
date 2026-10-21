using System.Collections.Concurrent;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Api.IntegrationTests.Notifications;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Notifications;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace IncidentHub.Api.IntegrationTests.Outbox;

/// <summary>
/// Channel routing end to end (13_02): real domain changes write outbox rows, the real runner dispatches them through the real handlers
/// and router. Email goes to Mailpit; Teams and Slack are recording fakes (the HTTP leg is covered by the 13_01 unit tests). Runs on its
/// own migrated database with a fake clock for the runner.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ChatDispatchTests(ApiFactory factory, MailpitFixture mailpit) : IAsyncLifetime, IClassFixture<MailpitFixture>
{
    private const string TeamsUrl = "https://contoso.webhook.office.com/webhookb2/secret-abc";
    private const string SlackUrl = "https://hooks.slack.com/services/T000/B000/secret-XXXX";
    private const string TeamsPrefs = "{\"channel\":\"Teams\"}";
    private const string SlackPrefs = "{\"channel\":\"Slack\"}";

    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
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

    // ---- User preference replaces the mail ----

    [Fact]
    public async Task Assign_TeamsUser_ThenDispatch_PostsToTeamsAndSendsNoMail()
    {
        var chat = new Recorders();
        var seed = await SeedIncidentAsync(assigneePrefs: TeamsPrefs, teamUrl: TeamsUrl);
        await ChangeAsync(seed, i => i.Assign(seed.AssigneeId, null, seed.Responder, Before));
        await using var worker = Worker(chat);

        await worker.RunAsync();

        var posts = chat.Teams.Posted.Where(n => n.Subject.Contains("Assigned to you")).ToList();
        posts.Should().ContainSingle().Which.ToAddress.Should().Be(TeamsUrl);
        (await mailpit.ListAsync(seed.Token)).Should().NotContain(m => m.Subject.Contains("Assigned to you"));
        chat.Slack.Posted.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_SlackUser_ThenDispatch_PostsToSlackAndSendsNoMail()
    {
        var chat = new Recorders();
        var seed = await SeedIncidentAsync(reporterPrefs: SlackPrefs, teamUrl: SlackUrl);
        await ChangeAsync(seed, i => i.TransitionTo(IncidentStatus.Triaged, seed.Responder, null, Before));
        await ChangeAsync(seed, i => i.TransitionTo(IncidentStatus.InProgress, seed.Responder, null, Before));
        await ChangeAsync(seed, i => i.TransitionTo(IncidentStatus.Resolved, seed.Responder, "Gateway timeout raised", Before));
        await using var worker = Worker(chat);

        await worker.RunAsync();

        chat.Slack.Posted.Where(n => n.Subject.Contains("Resolved:")).Should().ContainSingle().Which.ToAddress.Should().Be(SlackUrl);
        (await mailpit.ListAsync(seed.Token)).Should().NotContain(m => m.Subject.Contains("Resolved:"));
        chat.Teams.Posted.Should().BeEmpty();
    }

    [Fact]
    public async Task SlaBreached_TeamsContact_ThenDispatch_PostsToChannel()
    {
        var chat = new Recorders();
        var seed = await SeedIncidentAsync(contactPrefs: TeamsPrefs, teamUrl: TeamsUrl);
        await ChangeAsync(seed, i => i.MarkSlaBreached(Before));
        await using var worker = Worker(chat);

        await worker.RunAsync();

        chat.Teams.Posted.Where(n => n.Subject.Contains("SLA BREACHED")).Should().ContainSingle();
        (await mailpit.ListAsync(seed.Token)).Should().NotContain(m => m.Subject.Contains("SLA BREACHED"));
    }

    [Fact]
    public async Task SlaWarning_SlackAssignee_ThenDispatch_PostsToChannel()
    {
        var chat = new Recorders();
        var seed = await SeedIncidentAsync(assigneePrefs: SlackPrefs, teamUrl: SlackUrl);
        await ChangeAsync(seed, i => i.Assign(seed.AssigneeId, null, seed.Responder, Before));
        await ChangeAsync(seed, i => i.MarkSlaWarned(Before));
        await using var worker = Worker(chat);

        await worker.RunAsync();

        chat.Slack.Posted.Where(n => n.Subject.Contains("SLA warning")).Should().ContainSingle();
        (await mailpit.ListAsync(seed.Token)).Should().NotContain(m => m.Subject.Contains("SLA warning"));
    }

    // ---- Team events: mail and channel ----

    [Fact]
    public async Task Report_TeamWithUrl_ThenDispatch_MailsAndPosts()
    {
        var chat = new Recorders();
        var seed = await SeedIncidentAsync(teamUrl: TeamsUrl);
        await using var worker = Worker(chat);

        await worker.RunAsync();

        (await mailpit.ListAsync(seed.Token)).Should().ContainSingle().Which.To.Should().Be(seed.TeamEmail);
        chat.Teams.Posted.Should().ContainSingle().Which.ToAddress.Should().Be(TeamsUrl);
    }

    [Fact]
    public async Task Report_TeamWithoutUrl_ThenDispatch_MailsOnly()
    {
        var chat = new Recorders();
        var seed = await SeedIncidentAsync();
        await using var worker = Worker(chat);

        await worker.RunAsync();

        (await mailpit.ListAsync(seed.Token)).Should().ContainSingle();
        chat.Teams.Posted.Should().BeEmpty();
        chat.Slack.Posted.Should().BeEmpty();
    }

    [Fact]
    public async Task SlaBreached_NoContact_ThenDispatch_MailsAndPostsToOwningTeam()
    {
        var chat = new Recorders();
        var seed = await SeedIncidentAsync(withContact: false, teamUrl: SlackUrl);
        await ChangeAsync(seed, i => i.MarkSlaBreached(Before));
        await using var worker = Worker(chat);

        await worker.RunAsync();

        (await mailpit.ListAsync(seed.Token)).Should().ContainSingle(m => m.Subject.Contains("SLA BREACHED")).Which.To.Should().Be(seed.TeamEmail);
        chat.Slack.Posted.Should().Contain(n => n.Subject.Contains("SLA BREACHED"));
    }

    // ---- Fallbacks ----

    [Fact]
    public async Task Assign_TeamsUserWithoutUrl_ThenDispatch_FallsBackToMail()
    {
        var chat = new Recorders();
        var seed = await SeedIncidentAsync(assigneePrefs: TeamsPrefs);
        await ChangeAsync(seed, i => i.Assign(seed.AssigneeId, null, seed.Responder, Before));
        await using var worker = Worker(chat);

        await worker.RunAsync();

        (await mailpit.ListAsync(seed.Token)).Should().ContainSingle(m => m.Subject.Contains("Assigned to you"))
            .Which.To.Should().Be(seed.AssigneeEmail);
        chat.Teams.Posted.Should().BeEmpty();
    }

    [Fact]
    public async Task Assign_TeamsUserWhileChatDisabled_ThenDispatch_FallsBackToMail()
    {
        var seed = await SeedIncidentAsync(assigneePrefs: TeamsPrefs, teamUrl: TeamsUrl);
        await ChangeAsync(seed, i => i.Assign(seed.AssigneeId, null, seed.Responder, Before));
        await using var worker = Worker(chat: null);

        await worker.RunAsync();

        (await mailpit.ListAsync(seed.Token)).Should().ContainSingle(m => m.Subject.Contains("Assigned to you"));
        (await LoadOutboxAsync("IncidentAssigned")).Should().ContainSingle().Which.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Dispatch_ChatRevoked_UserGetsMailAndMessageIsProcessed()
    {
        var chat = new Recorders();
        chat.Teams.Fault = _ => new NonRetryableOutboxException("Teams webhook at contoso.webhook.office.com answered 404.");
        var seed = await SeedIncidentAsync(assigneePrefs: TeamsPrefs, teamUrl: TeamsUrl);
        await ChangeAsync(seed, i => i.Assign(seed.AssigneeId, null, seed.Responder, Before));
        await using var worker = Worker(chat);

        await worker.RunAsync();

        (await mailpit.ListAsync(seed.Token)).Should().ContainSingle(m => m.Subject.Contains("Assigned to you"));
        var rows = await LoadOutboxAsync("IncidentAssigned");
        rows.Should().ContainSingle().Which.ProcessedAt.Should().NotBeNull();
        (await LoadAllOutboxAsync()).Should().OnlyContain(m => m.ProcessedAt != null && (m.Error == null || !m.Error.Contains("secret")));
    }

    [Fact]
    public async Task Report_TeamChatRevoked_MailStillSentAndMessageProcessed()
    {
        var chat = new Recorders();
        chat.Teams.Fault = _ => new NonRetryableOutboxException("Teams webhook at contoso.webhook.office.com answered 410.");
        var seed = await SeedIncidentAsync(teamUrl: TeamsUrl);
        await using var worker = Worker(chat);

        await worker.RunAsync();

        (await mailpit.ListAsync(seed.Token)).Should().ContainSingle();
        (await LoadOutboxAsync("IncidentReported")).Should().ContainSingle().Which.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Dispatch_ChatTransientFailure_RetriesAndDoesNotMail()
    {
        var chat = new Recorders();
        chat.Teams.Fault = n => n.Subject.Contains("Assigned to you") ? new NotificationDeliveryException("Teams webhook answered 503.") : null;
        var seed = await SeedIncidentAsync(assigneePrefs: TeamsPrefs, teamUrl: TeamsUrl);
        await ChangeAsync(seed, i => i.Assign(seed.AssigneeId, null, seed.Responder, Before));
        await using var worker = Worker(chat);

        await worker.RunAsync();

        var failed = (await LoadOutboxAsync("IncidentAssigned")).Should().ContainSingle().Subject;
        failed.ProcessedAt.Should().BeNull();
        failed.Attempts.Should().Be(1);
        failed.NextAttemptAt.Should().NotBeNull();
        failed.Error.Should().NotContain("secret");
        (await mailpit.ListAsync(seed.Token)).Should().NotContain(m => m.Subject.Contains("Assigned to you"));

        // The dispatcher keeps running: the other message of the same incident was handled meanwhile.
        (await LoadOutboxAsync("IncidentReported")).Should().ContainSingle().Which.ProcessedAt.Should().NotBeNull();

        chat.Teams.Fault = null;
        _time.Advance(TimeSpan.FromMinutes(2));
        (await worker.RunAsync()).Should().Be(1);

        chat.Teams.Posted.Where(n => n.Subject.Contains("Assigned to you")).Should().ContainSingle();
        (await mailpit.ListAsync(seed.Token)).Should().NotContain(m => m.Subject.Contains("Assigned to you"));
        (await LoadOutboxAsync("IncidentAssigned")).Should().ContainSingle().Which.ProcessedAt.Should().NotBeNull();
    }

    // ---- Concurrency, other types, redelivery ----

    [Fact]
    public async Task Dispatch_TwoRunners_OneDeliveryPerMessage()
    {
        var chat = new Recorders();
        var seeds = new List<Seeded>();
        for (var i = 0; i < 4; i++)
        {
            var seed = await SeedIncidentAsync(assigneePrefs: TeamsPrefs, teamUrl: TeamsUrl);
            await ChangeAsync(seed, incident => incident.Assign(seed.AssigneeId, null, seed.Responder, Before));
            seeds.Add(seed);
        }

        await using var first = Worker(chat);
        await using var second = Worker(chat);
        await Task.WhenAll(first.RunAsync(), second.RunAsync());
        await first.RunAsync();

        foreach (var seed in seeds)
        {
            chat.Teams.Posted.Where(n => n.Subject.Contains("Assigned to you") && n.Subject.Contains(seed.Token)).Should().ContainSingle();
        }

        (await LoadOutboxAsync("IncidentAssigned")).Should().HaveCount(4).And.OnlyContain(m => m.ProcessedAt != null);
    }

    [Fact]
    public async Task Dispatch_CommentAdded_StillMarkedProcessedWithoutSending()
    {
        var chat = new Recorders();
        var seed = await SeedIncidentAsync(teamUrl: TeamsUrl);
        await using (var db = NewContext())
        {
            await db.OutboxMessages.ExecuteDeleteAsync();
        }

        await ChangeAsync(seed, i => i.AddComment("Looking into it", isInternal: false, seed.Reporter, Before));
        await using var worker = Worker(chat);

        await worker.RunAsync();

        (await mailpit.ListAsync(seed.Token)).Should().BeEmpty();
        chat.Teams.Posted.Should().BeEmpty();
        chat.Slack.Posted.Should().BeEmpty();
        (await LoadOutboxAsync("CommentAdded")).Should().ContainSingle().Which.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Dispatch_EmailLegRedelivered_SameMessageId()
    {
        var chat = new Recorders();
        var seed = await SeedIncidentAsync(teamUrl: TeamsUrl);
        await using var worker = Worker(chat);
        await worker.RunAsync();

        await using (var db = NewContext())
        {
            await db.OutboxMessages.Where(m => m.Type == "IncidentReported")
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, (DateTimeOffset?)null).SetProperty(m => m.Attempts, 0));
        }

        await worker.RunAsync();

        var ids = await mailpit.ListMessageIdsAsync($"incident reported: Checkout fails {seed.Token}");
        ids.Should().HaveCount(2);
        ids.Distinct().Should().ContainSingle("a redelivery carries the same Message-Id");
        chat.Teams.Posted.Should().HaveCount(2).And.OnlyContain(n => n.MessageId == chat.Teams.Posted[0].MessageId);
    }

    // ---- Helpers ----

    private static AppDbContext NewContext(string? connectionString = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString ?? _connectionString).Options);

    private WorkerHarness Worker(Recorders? chat) => new(_connectionString!, mailpit.SmtpHost, mailpit.SmtpPort, _time, chat);

    private static async Task<List<OutboxMessage>> LoadOutboxAsync(string type)
    {
        await using var db = NewContext();
        return await db.OutboxMessages.AsNoTracking().Where(m => m.Type == type).OrderBy(m => m.OccurredAt).ToListAsync();
    }

    private static async Task<List<OutboxMessage>> LoadAllOutboxAsync()
    {
        await using var db = NewContext();
        return await db.OutboxMessages.AsNoTracking().ToListAsync();
    }

    private static async Task ChangeAsync(Seeded seed, Action<Incident> change)
    {
        await using var db = NewContext();
        var incident = await db.Incidents.SingleAsync(i => i.Id == seed.IncidentId);
        change(incident);
        await db.SaveChangesAsync();
    }

    /// <summary>A team (optionally with a webhook), four users with the given preferences, an application and one reported incident.</summary>
    private static async Task<Seeded> SeedIncidentAsync(
        string? reporterPrefs = null,
        string? assigneePrefs = null,
        string? contactPrefs = null,
        string? teamUrl = null,
        bool withContact = true)
    {
        var token = Guid.NewGuid().ToString("N");
        await using var db = NewContext();

        var team = Team.Create($"Team {token}", $"team-{token}@example.com", teamUrl);
        var reporter = User.Create($"rep-{token}", "Rita Reporter", $"reporter-{token}@example.com", Before);
        var responder = User.Create($"res-{token}", "Rob Responder", $"responder-{token}@example.com", Before);
        var assignee = User.Create($"ass-{token}", "Ana Assignee", $"assignee-{token}@example.com", Before);
        var contact = User.Create($"con-{token}", "Lee Contact", $"contact-{token}@example.com", Before);
        db.Teams.Add(team);
        db.Users.AddRange(reporter, responder, assignee, contact);
        await db.SaveChangesAsync();

        foreach (var (user, prefs) in new[] { (reporter, reporterPrefs), (assignee, assigneePrefs), (contact, contactPrefs) })
        {
            if (prefs is not null)
            {
                await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.NotificationPrefs, prefs));
            }
        }

        var app = MonitoredApp.Create(
            $"App {token}", $"A{token[..12]}", team.Id, withContact ? contact.Id : null, [AppEnvironment.Production]);
        db.Applications.Add(app);
        await db.SaveChangesAsync();

        var reporterActor = new Actor(reporter.Id, new HashSet<UserRole> { UserRole.Reporter });
        var responderActor = new Actor(responder.Id, new HashSet<UserRole> { UserRole.Responder });
        var incident = Incident.Report(
            app.Id,
            AppEnvironment.Production,
            Severity.High,
            $"Checkout fails {token}",
            "<p>Error page after paying.</p>",
            null,
            IncidentSource.Manual,
            reporterActor,
            Before);
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        return new Seeded(
            incident.Id, token, $"team-{token}@example.com", $"assignee-{token}@example.com", assignee.Id, reporterActor, responderActor);
    }

    private sealed record Seeded(
        Guid IncidentId,
        string Token,
        string TeamEmail,
        string AssigneeEmail,
        Guid AssigneeId,
        Actor Reporter,
        Actor Responder);

    /// <summary>A live Teams or Slack sender that records what it is given and can be told to fail.</summary>
    private sealed class RecordingChatSender(NotificationChannel channel) : INotificationSender
    {
        private readonly ConcurrentQueue<Notification> _posted = new();

        public NotificationChannel Channel => channel;

        public bool IsLive => true;

        /// <summary>Returns the exception to throw for a notification, or null to accept it.</summary>
        public Func<Notification, Exception?>? Fault { get; set; }

        public IReadOnlyList<Notification> Posted => _posted.ToList();

        public Task SendAsync(Notification notification, CancellationToken cancellationToken)
        {
            if (Fault?.Invoke(notification) is { } fault)
            {
                throw fault;
            }

            _posted.Enqueue(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class Recorders
    {
        public RecordingChatSender Teams { get; } = new(NotificationChannel.Teams);

        public RecordingChatSender Slack { get; } = new(NotificationChannel.Slack);
    }

    /// <summary>
    /// The Worker's wiring in miniature: its own service provider, the real email sender pointed at Mailpit, and either recording chat
    /// senders (chat enabled) or the stock logging senders (<paramref name="chat"/> null, chat disabled).
    /// </summary>
    private sealed class WorkerHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly FakeTimeProvider _time;

        public WorkerHarness(string connectionString, string smtpHost, int smtpPort, FakeTimeProvider time, Recorders? chat)
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
            if (chat is not null)
            {
                // The stock logging senders stay registered but are not live, so the router picks these.
                services.AddSingleton<INotificationSender>(chat.Teams);
                services.AddSingleton<INotificationSender>(chat.Slack);
            }

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
