using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;
using Microsoft.Extensions.Options;

namespace IncidentHub.Application.Common.Sla;

/// <summary>Same rules as the <c>SlaPolicy</c> guards, reported per field so a bad config fails host start with a clear message.</summary>
public sealed class SlaDefaultsOptionsValidator : IValidateOptions<SlaDefaultsOptions>
{
    public ValidateOptionsResult Validate(string? name, SlaDefaultsOptions options)
    {
        var failures = new List<string>();

        foreach (var severity in Enum.GetValues<Severity>())
        {
            var path = $"{SlaDefaultsOptions.SectionName}:{severity}";
            var target = options.For(severity);

            if (target.AckMinutes <= 0)
            {
                failures.Add($"{path}:AckMinutes must be greater than 0.");
            }

            if (target.ResolveMinutes <= 0)
            {
                failures.Add($"{path}:ResolveMinutes must be greater than 0.");
            }
            else if (target.ResolveMinutes < target.AckMinutes)
            {
                failures.Add($"{path}:ResolveMinutes must be greater than or equal to AckMinutes.");
            }

            if (target.EscalateAtPercent is < SlaPolicy.MinEscalatePercent or > SlaPolicy.MaxEscalatePercent)
            {
                failures.Add($"{path}:EscalateAtPercent must be between {SlaPolicy.MinEscalatePercent} and {SlaPolicy.MaxEscalatePercent}.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
