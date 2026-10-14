using FluentAssertions;
using IncidentHub.Application.Outbox;
using IncidentHub.Infrastructure.Outbox;
using IncidentHub.Worker.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace IncidentHub.Worker.UnitTests.Jobs;

public sealed class OutboxDispatcherJobTests
{
    private static readonly OutboxDispatcherOptions Settings = new() { BatchSize = 50, PollInterval = TimeSpan.FromSeconds(2) };

    private readonly IOutboxDispatchRunner _runner = Substitute.For<IOutboxDispatchRunner>();
    private readonly FakeTimeProvider _time = new();

    private OutboxDispatcher NewJob()
    {
        var services = new ServiceCollection().AddScoped(_ => _runner).BuildServiceProvider();
        return new OutboxDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(Settings),
            _time,
            NullLogger<OutboxDispatcher>.Instance);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        return condition();
    }

    [Fact]
    public async Task Job_FullBatch_LoopsWithoutWaiting()
    {
        var calls = 0;
        _runner.RunOnceAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(++calls < 3 ? Settings.BatchSize : 0));
        var job = NewJob();

        await job.StartAsync(CancellationToken.None);
        var ranThree = await WaitUntilAsync(() => calls >= 3);

        // The clock never advanced, so the two extra passes cannot have come from the poll delay.
        ranThree.Should().BeTrue();
        await job.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Job_PartialBatch_WaitsForPollInterval()
    {
        var calls = 0;
        _runner.RunOnceAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(1);
        });
        var job = NewJob();

        await job.StartAsync(CancellationToken.None);
        (await WaitUntilAsync(() => calls == 1)).Should().BeTrue();
        await Task.Delay(100);
        calls.Should().Be(1);

        _time.Advance(Settings.PollInterval);
        (await WaitUntilAsync(() => calls == 2)).Should().BeTrue();
        await job.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Job_RunnerThrows_KeepsRunning()
    {
        var calls = 0;
        _runner.RunOnceAsync(Arg.Any<CancellationToken>()).Returns<Task<int>>(_ =>
        {
            return Interlocked.Increment(ref calls) == 1
                ? throw new InvalidOperationException("sql down")
                : Task.FromResult(0);
        });
        var job = NewJob();

        await job.StartAsync(CancellationToken.None);
        (await WaitUntilAsync(() => calls == 1)).Should().BeTrue();
        _time.Advance(Settings.PollInterval);
        var recovered = await WaitUntilAsync(() => calls >= 2);

        recovered.Should().BeTrue();
        await job.StopAsync(CancellationToken.None);
    }
}
