using System.Globalization;

namespace IncidentHub.Domain.Incidents;

/// <summary>Human-friendly incident number, displayed as <c>INC-1042</c>.</summary>
public readonly record struct IncidentNumber(int Value)
{
    private const string Prefix = "INC-";

    public static bool TryParse(string? text, out IncidentNumber number)
    {
        number = default;
        if (text is null || !text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(text.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return false;
        }

        number = new IncidentNumber(value);
        return true;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Prefix}{Value}");
}
