namespace IncidentHub.Application.Incidents;

internal static class IncidentMessages
{
    // One message for unknown, inactive and not-a-member so the response never reveals that an application exists.
    public const string ApplicationUnavailable = "Application not found or not available to you.";

    // Unknown, malformed and not-visible incident numbers all look the same.
    public const string IncidentNotFound = "Incident not found.";

    public const string EnvironmentNotOffered = "The application is not offered in this environment.";

    public const string AssigneeNotOnTeam = "The assignee must be a member of the application's team.";

    public const string TeamNotFound = "Team not found.";

    public const string DuplicateTargetInvalid = "The original incident must be a different incident you can see.";
}
