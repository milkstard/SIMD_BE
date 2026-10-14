using Microsoft.Extensions.Options;

namespace IncidentHub.Application.Outbox;

public sealed class OutboxDispatcherOptionsValidator : IValidateOptions<OutboxDispatcherOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxDispatcherOptions options)
    {
        var failures = new List<string>();
        var section = OutboxDispatcherOptions.SectionName;

        if (options.PollInterval <= TimeSpan.Zero)
        {
            failures.Add($"{section}:PollInterval must be greater than 0.");
        }

        if (options.BatchSize is <= 0 or > OutboxDispatcherOptions.MaxBatchSize)
        {
            failures.Add($"{section}:BatchSize must be between 1 and {OutboxDispatcherOptions.MaxBatchSize}.");
        }

        if (options.MaxAttempts <= 0)
        {
            failures.Add($"{section}:MaxAttempts must be greater than 0.");
        }

        if (options.BackoffBase <= TimeSpan.Zero)
        {
            failures.Add($"{section}:BackoffBase must be greater than 0.");
        }

        if (options.BackoffCap <= TimeSpan.Zero)
        {
            failures.Add($"{section}:BackoffCap must be greater than 0.");
        }
        else if (options.BackoffCap < options.BackoffBase)
        {
            failures.Add($"{section}:BackoffCap must be greater than or equal to BackoffBase.");
        }
        else if (options.BackoffCap > OutboxDispatcherOptions.MaxBackoffCap)
        {
            failures.Add($"{section}:BackoffCap must be at most {OutboxDispatcherOptions.MaxBackoffCap}.");
        }

        if (options.HandlerTimeout <= TimeSpan.Zero || options.HandlerTimeout > OutboxDispatcherOptions.MaxHandlerTimeout)
        {
            failures.Add($"{section}:HandlerTimeout must be greater than 0 and at most {OutboxDispatcherOptions.MaxHandlerTimeout}.");
        }

        if (options.PassBudget <= TimeSpan.Zero || options.PassBudget > OutboxDispatcherOptions.MaxPassBudget)
        {
            failures.Add($"{section}:PassBudget must be greater than 0 and at most {OutboxDispatcherOptions.MaxPassBudget}.");
        }

        if (options.JitterFraction is < 0 or > 1 || double.IsNaN(options.JitterFraction))
        {
            failures.Add($"{section}:JitterFraction must be between 0 and 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
