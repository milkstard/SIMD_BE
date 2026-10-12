using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Api.Contracts;

/// <summary>Nullable members let a missing field reach the validator (422 with a field key) instead of a model-binding 400.</summary>
public sealed record ReportIncidentRequest(
    Guid? ApplicationId,
    AppEnvironment? Environment,
    Severity? Severity,
    string? Title,
    string? Description,
    string? StepsToReproduce,
    List<Guid>? AttachmentIds);
