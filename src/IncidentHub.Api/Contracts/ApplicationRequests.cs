using IncidentHub.Domain.Apps;

namespace IncidentHub.Api.Contracts;

/// <summary>Nullable members let a missing field reach the validator (422 with a field key) instead of a model-binding 400.</summary>
public sealed record CreateApplicationRequest(
    string? Name,
    string? Code,
    Guid? OwningTeamId,
    Guid? EscalationContactId,
    List<AppEnvironment>? Environments,
    bool? IsActive);

/// <summary>Full replace (PUT); the expected RowVersion travels in <c>If-Match</c>.</summary>
public sealed record UpdateApplicationRequest(
    string? Name,
    string? Code,
    Guid? OwningTeamId,
    Guid? EscalationContactId,
    List<AppEnvironment>? Environments,
    bool? IsActive);
