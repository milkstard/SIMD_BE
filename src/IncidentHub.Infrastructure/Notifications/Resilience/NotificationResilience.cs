using IncidentHub.Application.Notifications;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace IncidentHub.Infrastructure.Notifications.Resilience;

/// <summary>
/// The Polly pipeline of one channel: total timeout, retry with jitter, circuit breaker, per-attempt timeout (outermost first).
/// Only transient failures are handled: a <see cref="Application.Outbox.NonRetryableOutboxException"/> passes straight through.
/// </summary>
public static class NotificationResilience
{
    public const string EmailPipeline = "email";

    public static void Configure(ResiliencePipelineBuilder builder, NotificationResilienceOptions options)
    {
        var transient = new PredicateBuilder()
            .Handle<NotificationDeliveryException>()
            .Handle<TimeoutRejectedException>();

        builder.AddTimeout(options.TotalTimeout);

        // Polly rejects zero retries, so "no retries" means no retry strategy.
        if (options.MaxRetries > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = transient,
                MaxRetryAttempts = options.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                Delay = options.RetryBaseDelay,
                UseJitter = true,
            });
        }

        builder
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = transient,
                FailureRatio = options.FailureRatio,
                MinimumThroughput = options.MinimumThroughput,
                SamplingDuration = options.SamplingDuration,
                BreakDuration = options.BreakDuration,
            })
            .AddTimeout(options.AttemptTimeout);
    }
}
