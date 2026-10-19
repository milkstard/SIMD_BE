using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Sla;

internal static class SlaPolicyIds
{
    /// <summary>Accepts a severity name only (any casing); a number such as "1" or a list such as "Critical,High" is not an id.</summary>
    public static bool TryParse(string? id, out Severity severity)
    {
        foreach (var candidate in Enum.GetValues<Severity>())
        {
            if (string.Equals(candidate.ToString(), id, StringComparison.OrdinalIgnoreCase))
            {
                severity = candidate;
                return true;
            }
        }

        severity = default;
        return false;
    }
}
