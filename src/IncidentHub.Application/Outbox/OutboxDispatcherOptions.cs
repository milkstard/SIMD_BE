namespace IncidentHub.Application.Outbox;

public sealed class OutboxDispatcherOptions
{
    public const string SectionName = "Outbox:Dispatcher";

    /// <summary>Upper bounds the validator enforces: a larger value locks too many rows or makes the timers throw at runtime.</summary>
    public const int MaxBatchSize = 1000;

    public static readonly TimeSpan MaxHandlerTimeout = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MaxPassBudget = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan MaxBackoffCap = TimeSpan.FromDays(1);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; set; } = 50;

    /// <summary>A message whose <c>Attempts</c> reach this value is parked: no longer claimed, <c>ProcessedAt</c> stays null.</summary>
    public int MaxAttempts { get; set; } = 8;

    public TimeSpan BackoffBase { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan BackoffCap { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Longest one message's handlers may run; past it the handlers are cancelled and the attempt counts as a failure.</summary>
    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// No further message is started once a pass has run this long (the rest wait for the next pass), so the transaction and its
    /// row locks end within <see cref="PassBudget"/> plus one <see cref="HandlerTimeout"/>.
    /// </summary>
    public TimeSpan PassBudget { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Random spread of each back-off delay, as a fraction (0.2 = plus or minus 20%).</summary>
    public double JitterFraction { get; set; } = 0.2;
}
