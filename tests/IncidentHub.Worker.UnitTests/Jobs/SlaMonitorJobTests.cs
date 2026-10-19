using FluentAssertions;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Sla.EvaluateSla;
using IncidentHub.Worker.Jobs;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace IncidentHub.Worker.UnitTests.Jobs;

public sealed class SlaMonitorJobTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IDistributedLock _locks = Substitute.For<IDistributedLock>();
    private readonly FakeTimeProvider _time = new();

    private SlaMonitor NewJob()
    {
        var services = new ServiceCollection().AddScoped(_ => _sender).BuildServiceProvider();
        return new SlaMonitor(
            services.GetRequiredService<IServiceScopeFactory>(),
            _locks,
            _time,
            NullLogger<SlaMonitor>.Instance);
    }

    private void LockIsFree() =>
        _locks.TryAcquireAsync(SlaMonitor.LockName, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IAsyncDisposable?>(Substitute.For<IAsyncDisposable>()));

    private void LockIsHeldElsewhere() =>
        _locks.TryAcquireAsync(SlaMonitor.LockName, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IAsyncDisposable?>(null));

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        return condition();
    }

    [Fact]
    public async Task SlaMonitorJob_LockHeldElsewhere_DoesNotEvaluate()
    {
        LockIsHeldElsewhere();
        var job = NewJob();

        await job.StartAsync(CancellationToken.None);
        var tried = await WaitUntilAsync(() => _locks.ReceivedCalls().Any());
        await job.StopAsync(CancellationToken.None);

        tried.Should().BeTrue();
        await _sender.DidNotReceive().Send(Arg.Any<IRequest<EvaluateSlaResult>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SlaMonitorJob_LockFree_EvaluatesOnStartAndOnEachTick()
    {
        LockIsFree();
        var calls = 0;
        _sender.Send(Arg.Any<IRequest<EvaluateSlaResult>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new EvaluateSlaResult(0, 0, 0));
            });
        var job = NewJob();

        await job.StartAsync(CancellationToken.None);
        (await WaitUntilAsync(() => calls == 1)).Should().BeTrue();
        await Task.Delay(100);
        calls.Should().Be(1);

        _time.Advance(SlaMonitor.Interval);
        (await WaitUntilAsync(() => calls == 2)).Should().BeTrue();
        await job.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SlaMonitorJob_EvaluationThrows_LoopContinues()
    {
        LockIsFree();
        var calls = 0;
        _sender.Send(Arg.Any<IRequest<EvaluateSlaResult>>(), Arg.Any<CancellationToken>())
            .Returns<Task<EvaluateSlaResult>>(_ =>
                Interlocked.Increment(ref calls) == 1
                    ? throw new InvalidOperationException("sql down")
                    : Task.FromResult(new EvaluateSlaResult(0, 0, 0)));
        var job = NewJob();

        await job.StartAsync(CancellationToken.None);
        (await WaitUntilAsync(() => calls == 1)).Should().BeTrue();
        _time.Advance(SlaMonitor.Interval);
        var recovered = await WaitUntilAsync(() => calls >= 2);

        recovered.Should().BeTrue();
        await job.StopAsync(CancellationToken.None);
    }
}
