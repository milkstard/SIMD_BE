namespace IncidentHub.Domain.Incidents;

public enum CloseReason
{
    Fixed = 1,
    Duplicate = 2,
    Invalid = 3,
    CannotReproduce = 4,
}
