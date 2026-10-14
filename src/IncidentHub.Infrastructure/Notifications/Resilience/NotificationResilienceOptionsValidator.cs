using Microsoft.Extensions.Options;

namespace IncidentHub.Infrastructure.Notifications.Resilience;

public sealed class NotificationResilienceOptionsValidator : IValidateOptions<NotificationResilienceOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationResilienceOptions options)
    {
        var failures = new List<string>();
        var section = NotificationResilienceOptions.SectionName;

        if (options.AttemptTimeout <= TimeSpan.Zero)
        {
            failures.Add($"{section}:AttemptTimeout must be greater than 0.");
        }

        if (options.MaxRetries is < 0 or > 10)
        {
            failures.Add($"{section}:MaxRetries must be between 0 and 10.");
        }

        if (options.RetryBaseDelay <= TimeSpan.Zero)
        {
            failures.Add($"{section}:RetryBaseDelay must be greater than 0.");
        }

        if (options.FailureRatio is <= 0 or > 1 || double.IsNaN(options.FailureRatio))
        {
            failures.Add($"{section}:FailureRatio must be greater than 0 and at most 1.");
        }

        if (options.MinimumThroughput < 2)
        {
            failures.Add($"{section}:MinimumThroughput must be at least 2.");
        }

        if (options.SamplingDuration < TimeSpan.FromSeconds(0.5))
        {
            failures.Add($"{section}:SamplingDuration must be at least 0.5 seconds.");
        }

        if (options.BreakDuration < TimeSpan.FromSeconds(0.5))
        {
            failures.Add($"{section}:BreakDuration must be at least 0.5 seconds.");
        }

        if (options.TotalTimeout <= TimeSpan.Zero)
        {
            failures.Add($"{section}:TotalTimeout must be greater than 0.");
        }
        else if (options.TotalTimeout < options.AttemptTimeout)
        {
            failures.Add($"{section}:TotalTimeout must be greater than or equal to AttemptTimeout.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
