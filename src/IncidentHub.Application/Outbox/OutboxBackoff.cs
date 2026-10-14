namespace IncidentHub.Application.Outbox;

public static class OutboxBackoff
{
    /// <summary>
    /// When a message that has just failed its <paramref name="attempts"/>-th attempt may be tried again:
    /// <c>min(cap, base * 2^(attempts-1))</c>, spread by plus or minus the jitter fraction (so a capped delay can exceed the cap by that fraction).
    /// </summary>
    public static DateTimeOffset NextAttemptAt(int attempts, DateTimeOffset now, OutboxDispatcherOptions options, Random jitter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(jitter);

        var exponent = Math.Clamp(attempts - 1, 0, 60);
        var seconds = Math.Min(options.BackoffCap.TotalSeconds, options.BackoffBase.TotalSeconds * Math.Pow(2, exponent));
        var spread = 1 + (options.JitterFraction * ((jitter.NextDouble() * 2) - 1));
        return now.AddSeconds(seconds * spread);
    }
}
