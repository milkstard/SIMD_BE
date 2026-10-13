namespace IncidentHub.Application.Incidents;

internal static class IncidentMessages
{
    // One message for unknown, inactive and not-a-member so the response never reveals that an application exists.
    public const string ApplicationUnavailable = "Application not found or not available to you.";
}
