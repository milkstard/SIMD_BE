using FluentAssertions;
using IncidentHub.Application.Notifications;
using IncidentHub.Application.Outbox;
using IncidentHub.Infrastructure.Notifications.Resilience;
using Microsoft.Extensions.Time.Testing;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace IncidentHub.Api.UnitTests.Notifications;

public sealed class NotificationResilienceTests
{
    private readonly FakeTimeProvider _time = new();

    private ResiliencePipeline Build(NotificationResilienceOptions options)
    {
        var builder = new ResiliencePipelineBuilder { TimeProvider = _time };
        NotificationResilience.Configure(builder, options);
        return builder.Build();
    }

    private static NotificationResilienceOptions Options(int retries = 3, int throughput = 5) => new()
    {
        MaxRetries = retries,
        RetryBaseDelay = TimeSpan.FromSeconds(1),
        AttemptTimeout = TimeSpan.FromSeconds(10),
        TotalTimeout = TimeSpan.FromSeconds(45),
        MinimumThroughput = throughput,
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(30),
    };

    /// <summary>Runs the action while advancing the fake clock, so retry delays and timeouts elapse.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        var task = action();
        for (var i = 0; i < 300 && !task.IsCompleted; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(1);
        }

        await task;
    }

    [Fact]
    public async Task Pipeline_TransientFailure_RetriesThenSucceeds()
    {
        var pipeline = Build(Options());
        var calls = 0;

        await RunAsync(() => pipeline.ExecuteAsync(async _ =>
        {
            calls++;
            if (calls < 3)
            {
                throw new NotificationDeliveryException("temporary");
            }

            await Task.CompletedTask;
        }).AsTask());

        calls.Should().Be(3);
    }

    [Fact]
    public async Task Pipeline_AlwaysFailing_StopsAfterMaxRetries()
    {
        var options = Options(retries: 2);
        options.MinimumThroughput = 100; // keep the breaker out of this test
        var pipeline = Build(options);
        var calls = 0;

        var act = () => RunAsync(() => pipeline.ExecuteAsync(_ =>
        {
            calls++;
            throw new NotificationDeliveryException("temporary");
        }).AsTask());

        await act.Should().ThrowAsync<NotificationDeliveryException>();
        calls.Should().Be(3, "one try and two retries");
    }

    [Fact]
    public async Task Pipeline_NonRetryable_NotRetried()
    {
        var pipeline = Build(Options());
        var calls = 0;

        var act = () => pipeline.ExecuteAsync(_ =>
        {
            calls++;
            throw new NonRetryableOutboxException("permanent");
        }).AsTask();

        await act.Should().ThrowAsync<NonRetryableOutboxException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Pipeline_RepeatedFailures_OpensCircuit()
    {
        var pipeline = Build(Options(retries: 0, throughput: 2));

        for (var i = 0; i < 2; i++)
        {
            await FluentActions.Awaiting(() => pipeline.ExecuteAsync(_ => throw new NotificationDeliveryException("down")).AsTask())
                .Should().ThrowAsync<NotificationDeliveryException>();
        }

        var called = false;
        var act = () => pipeline.ExecuteAsync(_ =>
        {
            called = true;
            return ValueTask.CompletedTask;
        }).AsTask();

        await act.Should().ThrowAsync<BrokenCircuitException>();
        called.Should().BeFalse("an open circuit rejects without calling");
    }

    [Fact]
    public async Task Pipeline_AfterBreak_HalfOpensAndCloses()
    {
        var options = Options(retries: 0, throughput: 2);
        var pipeline = Build(options);

        for (var i = 0; i < 2; i++)
        {
            await FluentActions.Awaiting(() => pipeline.ExecuteAsync(_ => throw new NotificationDeliveryException("down")).AsTask())
                .Should().ThrowAsync<NotificationDeliveryException>();
        }

        _time.Advance(options.BreakDuration + TimeSpan.FromSeconds(1));

        var calls = 0;
        for (var i = 0; i < 3; i++)
        {
            await pipeline.ExecuteAsync(_ =>
            {
                calls++;
                return ValueTask.CompletedTask;
            });
        }

        calls.Should().Be(3, "the trial call succeeds and the circuit closes again");
    }

    [Fact]
    public async Task Pipeline_SlowSend_TimesOut()
    {
        var pipeline = Build(Options(retries: 0));

        var act = () => RunAsync(() => pipeline.ExecuteAsync(async token => await Task.Delay(Timeout.Infinite, token)).AsTask());

        await act.Should().ThrowAsync<TimeoutRejectedException>();
    }
}
