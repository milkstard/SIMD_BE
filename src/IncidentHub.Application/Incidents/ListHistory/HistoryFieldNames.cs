using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.Incidents.ListHistory;

/// <summary>Maps the stored entity property name of a history row to the display name the API returns.</summary>
internal static class HistoryFieldNames
{
    public const string Assignee = "AssigneeId";
    public const string Team = "AssignedTeamId";
    public const string Application = "ApplicationId";
    public const string DuplicateOf = "DuplicateOfId";

    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        [Assignee] = "Assignee",
        [Team] = "Team",
        [Application] = "Application",
        [DuplicateOf] = "Duplicate of",
        ["StepsToReproduce"] = "Steps to reproduce",
        ["IsSlaBreached"] = "SLA breached",
        ["CloseReason"] = "Close reason",
        [Incident.CommentHistoryField] = "Comment",
        [Incident.InternalCommentHistoryField] = "Internal comment",
    };

    public static string ToDisplayName(string field) => Names.TryGetValue(field, out var name) ? name : field;
}
