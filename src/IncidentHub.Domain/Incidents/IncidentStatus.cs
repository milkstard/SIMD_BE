namespace IncidentHub.Domain.Incidents;

public enum IncidentStatus
{
    New = 1,
    Triaged = 2,
    InProgress = 3,
    OnHold = 4,
    Resolved = 5,
    Reopened = 6,
    Closed = 7,
}
