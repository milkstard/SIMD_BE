using System.Globalization;
using System.Text.RegularExpressions;

namespace IncidentHub.Application.Notifications.Email;

/// <summary>What one email says, before rendering: a subject, a headline and labelled facts. Values are plain text; the renderer encodes them.</summary>
public sealed record EmailContent(
    string Subject,
    string Headline,
    IReadOnlyList<(string Label, string Value)> Facts,
    string IncidentNumber);

/// <summary>
/// Wording of the BE-10 and BE-12 mails. The description is never included (rich HTML, possibly large) and neither is any comment.
/// The title is collapsed to one line, so it cannot add header or body lines.
/// </summary>
public static partial class EmailContentBuilder
{
    public static EmailContent Reported(IncidentEmailFacts incident) => Build(
        $"[{incident.Number}] {incident.Severity} incident reported: {OneLine(incident.Title)}",
        incident,
        note: null);

    public static EmailContent Assigned(IncidentEmailFacts incident) => Build(
        $"[{incident.Number}] Assigned to you: {OneLine(incident.Title)}",
        incident,
        note: null);

    public static EmailContent Resolved(IncidentEmailFacts incident, string? note) => Build(
        $"[{incident.Number}] Resolved: {OneLine(incident.Title)}",
        incident,
        note);

    public static EmailContent SlaWarning(IncidentEmailFacts incident)
    {
        var extra = new List<(string Label, string Value)>();
        if (incident.EscalateAtPercent is { } percent)
        {
            extra.Add(("Warning", $"{percent}% of the resolve window used"));
        }

        extra.Add(("Resolve due", DueAt(incident.ResolveDueAt)));
        return Build($"[{incident.Number}] SLA warning: {OneLine(incident.Title)}", incident, note: null, extra);
    }

    public static EmailContent SlaBreached(IncidentEmailFacts incident) => Build(
        $"[{incident.Number}] SLA BREACHED: {OneLine(incident.Title)}",
        incident,
        note: null,
        [("Resolve due", DueAt(incident.ResolveDueAt))]);

    private static EmailContent Build(
        string subject,
        IncidentEmailFacts incident,
        string? note,
        IEnumerable<(string Label, string Value)>? extraFacts = null)
    {
        var facts = new List<(string Label, string Value)>
        {
            ("Incident", $"{incident.Number} {OneLine(incident.Title)}"),
            ("Application", incident.ApplicationName),
            ("Environment", incident.Environment.ToString()),
            ("Severity", incident.Severity.ToString()),
            ("Status", incident.Status.ToString()),
        };

        if (extraFacts is not null)
        {
            facts.AddRange(extraFacts);
        }

        if (!string.IsNullOrWhiteSpace(note))
        {
            facts.Add(("Note", OneLine(note)));
        }

        return new EmailContent(subject, subject, facts, incident.Number);
    }

    private static string DueAt(DateTimeOffset? at) =>
        at is { } value
            ? value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : "unknown";

    private static string OneLine(string value) => LineBreaks().Replace(value, " ").Trim();

    [GeneratedRegex(@"\s*[\r\n\u0085  ]+\s*")]
    private static partial Regex LineBreaks();
}
