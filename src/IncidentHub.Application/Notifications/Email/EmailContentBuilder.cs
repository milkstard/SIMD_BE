namespace IncidentHub.Application.Notifications.Email;

/// <summary>What one email says, before rendering: a subject, a headline and labelled facts. Values are plain text; the renderer encodes them.</summary>
public sealed record EmailContent(
    string Subject,
    string Headline,
    IReadOnlyList<(string Label, string Value)> Facts,
    string IncidentNumber);

/// <summary>
/// Wording of the three BE-10 mails. The description is never included (rich HTML, possibly large) and neither is any comment.
/// </summary>
public static class EmailContentBuilder
{
    public static EmailContent Reported(IncidentEmailFacts incident) => Build(
        $"[{incident.Number}] {incident.Severity} incident reported: {incident.Title}",
        incident,
        note: null);

    public static EmailContent Assigned(IncidentEmailFacts incident) => Build(
        $"[{incident.Number}] Assigned to you: {incident.Title}",
        incident,
        note: null);

    public static EmailContent Resolved(IncidentEmailFacts incident, string? note) => Build(
        $"[{incident.Number}] Resolved: {incident.Title}",
        incident,
        note);

    private static EmailContent Build(string subject, IncidentEmailFacts incident, string? note)
    {
        var facts = new List<(string Label, string Value)>
        {
            ("Incident", $"{incident.Number} {incident.Title}"),
            ("Application", incident.ApplicationName),
            ("Environment", incident.Environment.ToString()),
            ("Severity", incident.Severity.ToString()),
            ("Status", incident.Status.ToString()),
        };

        if (!string.IsNullOrWhiteSpace(note))
        {
            facts.Add(("Note", note.Trim()));
        }

        return new EmailContent(subject, subject, facts, incident.Number);
    }
}
