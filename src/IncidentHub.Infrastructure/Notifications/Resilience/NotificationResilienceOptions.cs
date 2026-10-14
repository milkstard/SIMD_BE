namespace IncidentHub.Infrastructure.Notifications.Resilience;

public sealed class NotificationResilienceOptions
{
    public const string SectionName = "Notifications:Resilience";

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public int MaxRetries { get; set; } = 3;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    public double FailureRatio { get; set; } = 0.5;

    public int MinimumThroughput { get; set; } = 5;

    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Covers every attempt and delay of one send. Must stay below the outbox <c>HandlerTimeout</c> (60 s).</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(45);
}
