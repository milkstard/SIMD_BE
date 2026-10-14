using System.Collections.Concurrent;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Outbox;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Infrastructure.Outbox;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace IncidentHub.Api.IntegrationTests.Outbox;

/// <summary>
/// Runs the dispatch pass against its own migrated database: the runner claims rows globally, so it must not share a table with
/// the rows other tests leave behind. Handlers are fakes; time is a <see cref="FakeTimeProvider"/>.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OutboxDispatchRunnerTests(ApiFactory factory) : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

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

    // ---- Success, retry, parking ----

    [Fact]
    public async Task RunOnce_HandlerSucceeds_MarksProcessed()
    {
        var id = await SeedAsync();
        var handler = new RecordingHandler();

        var claimed = await RunAsync(handler);

        claimed.Should().Be(1);
        handler.Handled.Should().ContainSingle().Which.Should().Be(id);
        var row = await LoadAsync(id);
        row.ProcessedAt.Should().Be(T0);
        row.Error.Should().BeNull();
        (await RunAsync(handler)).Should().Be(0);
    }

    [Fact]
    public async Task RunOnce_HandlerPassesTheDeserializedEvent()
    {
        var id = await SeedAsync();
        IDomainEvent? seen = null;
        var handler = new RecordingHandler((e, _) =>
        {
            seen = e;
            return Task.CompletedTask;
        });

        await RunAsync(handler);

        seen.Should().BeOfType<IncidentReported>();
        handler.Handled.Should().Contain(id);
    }

    [Fact]
    public async Task RunOnce_HandlerThrows_IncrementsAttemptsKeepsRowAndSetsNextAttempt()
    {
        var id = await SeedAsync();

        await RunAsync(new RecordingHandler(Fail("smtp down")));

        var row = await LoadAsync(id);
        row.ProcessedAt.Should().BeNull();
        row.Attempts.Should().Be(1);
        row.Error.Should().Contain("smtp down");
        row.NextAttemptAt.Should().NotBeNull();
        (row.NextAttemptAt!.Value - T0).TotalSeconds.Should().BeInRange(8, 12);
    }

    [Fact]
    public async Task RunOnce_LongError_IsTruncated()
    {
        var id = await SeedAsync();

        await RunAsync(new RecordingHandler(Fail(new string('x', 5000))));

        (await LoadAsync(id)).Error!.Length.Should().Be(OutboxMessage.ErrorMaxLength);
    }

    [Fact]
    public async Task RunOnce_BeforeNextAttempt_DoesNotClaim()
    {
        await SeedAsync();
        var handler = new RecordingHandler(Fail("boom"));
        await RunAsync(handler);

        _time.Advance(TimeSpan.FromSeconds(5));
        var claimed = await RunAsync(handler);

        claimed.Should().Be(0);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RunOnce_AfterNextAttempt_RetriesAndSucceeds()
    {
        var id = await SeedAsync();
        var failFirst = true;
        var handler = new RecordingHandler((_, _) => failFirst ? Task.FromException(new InvalidOperationException("once")) : Task.CompletedTask);
        await RunAsync(handler);
        failFirst = false;

        _time.Advance(TimeSpan.FromMinutes(1));
        var claimed = await RunAsync(handler);

        claimed.Should().Be(1);
        var row = await LoadAsync(id);
        row.ProcessedAt.Should().NotBeNull();
        row.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task RunOnce_MaxAttemptsReached_ParksRow()
    {
        var id = await SeedAsync();
        var handler = new RecordingHandler(Fail("always"));
        var options = new OutboxDispatcherOptions { MaxAttempts = 2 };

        await RunAsync(handler, options);
        _time.Advance(TimeSpan.FromHours(2));
        await RunAsync(handler, options);
        _time.Advance(TimeSpan.FromHours(2));
        var claimed = await RunAsync(handler, options);

        claimed.Should().Be(0);
        handler.Calls.Should().Be(2);
        var row = await LoadAsync(id);
        row.Attempts.Should().Be(2);
        row.ProcessedAt.Should().BeNull();
        row.Error.Should().Contain("always");
    }

    [Fact]
    public async Task RunOnce_NonRetryable_ParksImmediately()
    {
        var id = await SeedAsync();
        var handler = new RecordingHandler((_, _) => Task.FromException(new NonRetryableOutboxException("no recipient")));

        await RunAsync(handler);
        _time.Advance(TimeSpan.FromHours(2));
        var claimed = await RunAsync(handler);

        claimed.Should().Be(0);
        handler.Calls.Should().Be(1);
        var row = await LoadAsync(id);
        row.Attempts.Should().Be(new OutboxDispatcherOptions().MaxAttempts);
        row.ProcessedAt.Should().BeNull();
        row.Error.Should().Contain("no recipient");
    }

    // ---- Routing ----

    [Fact]
    public async Task RunOnce_NoHandler_MarksProcessed()
    {
        var id = await SeedAsync();

        await RunAsync();

        var row = await LoadAsync(id);
        row.ProcessedAt.Should().NotBeNull();
        row.Error.Should().BeNull();
    }

    [Fact]
    public async Task RunOnce_HandlerForOtherType_IsNotCalled()
    {
        var id = await SeedAsync();
        var handler = new RecordingHandler(eventType: nameof(CommentAdded));

        await RunAsync(handler);

        handler.Calls.Should().Be(0);
        (await LoadAsync(id)).ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunOnce_UnknownType_Parks()
    {
        var id = Guid.NewGuid();
        await InsertRawAsync(id, "NoSuchEvent", "{}", T0.AddMinutes(-1));
        var okId = await SeedAsync();

        await RunAsync(new RecordingHandler());

        var row = await LoadAsync(id);
        row.ProcessedAt.Should().BeNull();
        row.Attempts.Should().Be(new OutboxDispatcherOptions().MaxAttempts);
        row.Error.Should().NotBeNull();
        (await LoadAsync(okId)).ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunOnce_UnreadablePayload_Parks()
    {
        var id = Guid.NewGuid();
        await InsertRawAsync(id, nameof(IncidentReported), "not json", T0.AddMinutes(-1));

        await RunAsync(new RecordingHandler());

        var row = await LoadAsync(id);
        row.ProcessedAt.Should().BeNull();
        row.Attempts.Should().Be(new OutboxDispatcherOptions().MaxAttempts);
    }

    // ---- Batching and isolation ----

    [Fact]
    public async Task RunOnce_OneFailure_OthersStillProcessed()
    {
        var first = await SeedAsync(T0.AddMinutes(-3));
        var bad = await SeedAsync(T0.AddMinutes(-2));
        var last = await SeedAsync(T0.AddMinutes(-1));
        var handler = new RecordingHandler((_, id) => id == bad ? Task.FromException(new InvalidOperationException("bad row")) : Task.CompletedTask);

        var claimed = await RunAsync(handler);

        claimed.Should().Be(3);
        (await LoadAsync(first)).ProcessedAt.Should().NotBeNull();
        (await LoadAsync(last)).ProcessedAt.Should().NotBeNull();
        var failed = await LoadAsync(bad);
        failed.ProcessedAt.Should().BeNull();
        failed.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task RunOnce_MoreThanBatch_ClaimsBatchSizeOldestFirst()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await SeedAsync(T0.AddMinutes(-10 + i)));
        }

        var handler = new RecordingHandler();
        var claimed = await RunAsync(handler, new OutboxDispatcherOptions { BatchSize = 3 });

        claimed.Should().Be(3);
        handler.Handled.Should().Equal(ids.Take(3));
        (await LoadAsync(ids[3])).ProcessedAt.Should().BeNull();
        (await LoadAsync(ids[4])).ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task RunOnce_TwoRunnersConcurrently_EachMessageHandledOnce()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            ids.Add(await SeedAsync(T0.AddMinutes(-10 + i)));
        }

        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var shared = new ConcurrentQueue<Guid>();
        var options = new OutboxDispatcherOptions { BatchSize = 3 };

        // Runner A holds its rows (UPDLOCK) until released; runner B must skip them (READPAST) and handle none of them.
        // The index is keyed on ProcessedAt, so the ordered claim reads and locks every eligible row, not only the batch:
        // B therefore finds nothing while A is mid-pass. A second pass after A commits picks up the rest.
        var holding = new RecordingHandler(
            async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
            },
            shared);
        var second = new RecordingHandler(sink: shared);

        var runnerA = RunAsync(holding, options);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var claimedByB = await RunAsync(second, options);
        release.SetResult();
        var claimedByA = await runnerA;

        claimedByA.Should().Be(3);
        claimedByB.Should().Be(0);
        second.Calls.Should().Be(0);
        shared.Should().HaveCount(3).And.OnlyHaveUniqueItems();

        await RunAsync(second, options);

        shared.Should().HaveCount(6).And.OnlyHaveUniqueItems();
        shared.Should().BeEquivalentTo(ids);
        foreach (var id in ids)
        {
            (await LoadAsync(id)).ProcessedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task RunOnce_CommitFails_RowsStayClaimable()
    {
        var ids = new[] { await SeedAsync(T0.AddMinutes(-2)), await SeedAsync(T0.AddMinutes(-1)) };
        var handler = new RecordingHandler();

        await using (var failingDb = NewContext(interceptor: new ThrowOnSave()))
        {
            var failing = NewRunner(failingDb, handler, new OutboxDispatcherOptions());
            var run = () => failing.RunOnceAsync(CancellationToken.None);
            await run.Should().ThrowAsync<InvalidOperationException>();
        }

        foreach (var id in ids)
        {
            var row = await LoadAsync(id);
            row.ProcessedAt.Should().BeNull();
            row.Attempts.Should().Be(0);
        }

        // At-least-once: the handlers ran for the rolled-back batch and run again for the retry.
        var claimed = await RunAsync(handler);
        claimed.Should().Be(2);
        handler.Calls.Should().Be(4);
    }

    // ---- Time limits, shutdown, handler scope ----

    [Fact]
    public async Task RunOnce_HandlerTimesOut_CountsAsFailedAttempt()
    {
        var id = await SeedAsync();
        var handler = new RecordingHandler(withToken: (_, token) => Task.Delay(Timeout.Infinite, token));

        var claimed = await RunAsync(handler, new OutboxDispatcherOptions { HandlerTimeout = TimeSpan.FromMilliseconds(100) });

        claimed.Should().Be(1);
        var row = await LoadAsync(id);
        row.ProcessedAt.Should().BeNull();
        row.Attempts.Should().Be(1);
        row.Error.Should().Contain("timed out");
        row.NextAttemptAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunOnce_PassBudgetSpent_StartsNoFurtherMessage()
    {
        var ids = new[] { await SeedAsync(T0.AddMinutes(-3)), await SeedAsync(T0.AddMinutes(-2)), await SeedAsync(T0.AddMinutes(-1)) };
        var handler = new RecordingHandler(withToken: (_, _) =>
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            return Task.CompletedTask;
        });
        var options = new OutboxDispatcherOptions { PassBudget = TimeSpan.FromMinutes(1) };

        var claimed = await RunAsync(handler, options);

        claimed.Should().Be(3);
        handler.Calls.Should().Be(1);
        (await LoadAsync(ids[0])).ProcessedAt.Should().NotBeNull();
        foreach (var id in ids.Skip(1))
        {
            var row = await LoadAsync(id);
            row.ProcessedAt.Should().BeNull();
            row.Attempts.Should().Be(0);
        }

        (await RunAsync(handler, options)).Should().Be(2);
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task RunOnce_HostStops_SavesOutcomesAlreadyRecordedAndLeavesTheRest()
    {
        var ids = new[] { await SeedAsync(T0.AddMinutes(-3)), await SeedAsync(T0.AddMinutes(-2)), await SeedAsync(T0.AddMinutes(-1)) };
        using var stop = new CancellationTokenSource();
        var handler = new RecordingHandler(withToken: async (call, token) =>
        {
            if (call == 2)
            {
                await stop.CancelAsync();
                await Task.Delay(Timeout.Infinite, token);
            }
        });

        var claimed = await RunAsync(handler, cancellationToken: stop.Token);

        claimed.Should().Be(3);
        handler.Calls.Should().Be(2);
        (await LoadAsync(ids[0])).ProcessedAt.Should().NotBeNull();
        foreach (var id in ids.Skip(1))
        {
            var row = await LoadAsync(id);
            row.ProcessedAt.Should().BeNull();
            row.Attempts.Should().Be(0);
        }

        // The locks are gone with the transaction: the unfinished rows are claimed again.
        (await RunAsync(new RecordingHandler())).Should().Be(2);
    }

    [Fact]
    public async Task RunOnce_HostStopsWhileLastHandlerFinishes_StillSavesItsOutcome()
    {
        var id = await SeedAsync();
        using var stop = new CancellationTokenSource();
        var handler = new RecordingHandler(withToken: async (_, _) => await stop.CancelAsync());

        var claimed = await RunAsync(handler, cancellationToken: stop.Token);

        claimed.Should().Be(1);
        var row = await LoadAsync(id);
        row.ProcessedAt.Should().NotBeNull();
        row.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task RunOnce_HostAlreadyStopping_StartsNoMessageButSavesWhatIsDone()
    {
        var ids = new[] { await SeedAsync(T0.AddMinutes(-2)), await SeedAsync(T0.AddMinutes(-1)) };
        using var stop = new CancellationTokenSource();
        var handler = new RecordingHandler(withToken: async (_, _) => await stop.CancelAsync());

        var claimed = await RunAsync(handler, cancellationToken: stop.Token);

        claimed.Should().Be(2);
        handler.Calls.Should().Be(1);
        (await LoadAsync(ids[0])).ProcessedAt.Should().NotBeNull();
        (await LoadAsync(ids[1])).ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task RunOnce_Handlers_AreResolvedInAScopePerMessage()
    {
        await SeedAsync(T0.AddMinutes(-2));
        await SeedAsync(T0.AddMinutes(-1));
        var instances = new List<RecordingHandler>();
        var services = new ServiceCollection().AddScoped<IOutboxMessageHandler>(_ =>
        {
            var created = new RecordingHandler();
            instances.Add(created);
            return created;
        });

        await using var db = NewContext();
        await NewRunner(db, services, new OutboxDispatcherOptions()).RunOnceAsync(CancellationToken.None);

        instances.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        instances.Should().OnlyContain(h => h.Calls == 1);
    }

    // ---- Helpers ----

    private static Func<IDomainEvent, Guid, Task> Fail(string message) =>
        (_, _) => Task.FromException(new InvalidOperationException(message));

    private static AppDbContext NewContext(string? connectionString = null, IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString ?? _connectionString);
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new AppDbContext(builder.Options);
    }

    private OutboxDispatchRunner NewRunner(AppDbContext db, IOutboxMessageHandler? handler, OutboxDispatcherOptions options)
    {
        var services = new ServiceCollection();
        if (handler is not null)
        {
            services.AddSingleton(handler);
        }

        return NewRunner(db, services, options);
    }

    private OutboxDispatchRunner NewRunner(AppDbContext db, IServiceCollection services, OutboxDispatcherOptions options) =>
        new(
            db,
            new OutboxEventTypes(),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            _time,
            NullLogger<OutboxDispatchRunner>.Instance);

    private async Task<int> RunAsync(
        IOutboxMessageHandler? handler = null,
        OutboxDispatcherOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = NewContext();
        return await NewRunner(db, handler, options ?? new OutboxDispatcherOptions()).RunOnceAsync(cancellationToken);
    }

    private static async Task<Guid> SeedAsync(DateTimeOffset? occurredAt = null)
    {
        var message = OutboxMessage.From(new IncidentReported(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AppEnvironment.Production,
            Severity.High,
            IncidentSource.Manual,
            Guid.NewGuid(),
            occurredAt ?? T0.AddMinutes(-1)));

        await using var db = NewContext();
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private static async Task InsertRawAsync(Guid id, string type, string payload, DateTimeOffset occurredAt)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO OutboxMessages (Id, Type, Payload, OccurredAt, Attempts) VALUES ({id}, {type}, {payload}, {occurredAt}, 0)");
    }

    private static async Task<OutboxMessage> LoadAsync(Guid id)
    {
        await using var db = NewContext();
        return await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private sealed class ThrowOnSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("commit failed");
    }

    private sealed class RecordingHandler(
        Func<IDomainEvent, Guid, Task>? action = null,
        ConcurrentQueue<Guid>? sink = null,
        string eventType = nameof(IncidentReported),
        Func<int, CancellationToken, Task>? withToken = null) : IOutboxMessageHandler
    {
        private int _calls;

        public ConcurrentQueue<Guid> Handled { get; } = sink ?? new ConcurrentQueue<Guid>();

        public int Calls => _calls;

        public string EventType { get; } = eventType;

        public async Task HandleAsync(IDomainEvent @event, Guid outboxMessageId, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            if (withToken is not null)
            {
                await withToken(call, cancellationToken);
            }

            if (action is not null)
            {
                await action(@event, outboxMessageId);
            }

            Handled.Enqueue(outboxMessageId);
        }
    }
}
