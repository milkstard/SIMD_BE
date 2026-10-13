using IncidentHub.Application.Incidents.UpdateIncident;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Api.Contracts;

/// <summary>
/// Body of <c>PATCH /incidents/{id}</c>. The JSON serializer calls a setter only for keys present in the body (an explicit
/// <c>null</c> included), so each setter records that its field was sent. Only <c>stepsToReproduce</c> and <c>priority</c>
/// accept <c>null</c>; on any other field it reaches the validator and answers 422.
/// </summary>
public sealed class UpdateIncidentRequest
{
    private Optional<string?> _title;
    private Optional<string?> _description;
    private Optional<string?> _stepsToReproduce;
    private Optional<Severity?> _severity;
    private Optional<Priority?> _priority;
    private Optional<Guid?> _applicationId;
    private Optional<AppEnvironment?> _environment;

    public string? Title
    {
        get => _title.Value;
        init => _title = new(true, value);
    }

    public string? Description
    {
        get => _description.Value;
        init => _description = new(true, value);
    }

    public string? StepsToReproduce
    {
        get => _stepsToReproduce.Value;
        init => _stepsToReproduce = new(true, value);
    }

    public Severity? Severity
    {
        get => _severity.Value;
        init => _severity = new(true, value);
    }

    public Priority? Priority
    {
        get => _priority.Value;
        init => _priority = new(true, value);
    }

    public Guid? ApplicationId
    {
        get => _applicationId.Value;
        init => _applicationId = new(true, value);
    }

    public AppEnvironment? Environment
    {
        get => _environment.Value;
        init => _environment = new(true, value);
    }

    /// <summary>Required with <c>severity</c>; ignored otherwise.</summary>
    public string? Reason { get; init; }

    public UpdateIncidentCommand ToCommand(Guid incidentId, byte[] expectedRowVersion) =>
        new(
            incidentId,
            _title,
            _description,
            _stepsToReproduce,
            _severity,
            _priority,
            _applicationId,
            _environment,
            Reason,
            expectedRowVersion);
}
