namespace IncidentHub.Application.Incidents.ListIncidents;

/// <summary>Contract tokens: <c>createdAt</c>, <c>-createdAt</c> (default), <c>severity</c> (most severe first), <c>-updatedAt</c>.</summary>
public enum IncidentSort
{
    CreatedAtDesc = 0,
    CreatedAtAsc,
    Severity,
    UpdatedAtDesc,
}
