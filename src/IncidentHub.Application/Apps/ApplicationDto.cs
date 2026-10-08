using IncidentHub.Application.Common;
using IncidentHub.Domain.Apps;

namespace IncidentHub.Application.Apps;

public sealed record ApplicationDto(
    Guid Id,
    string Name,
    string Code,
    TeamRefDto OwningTeam,
    UserRefDto? EscalationContact,
    IReadOnlyList<AppEnvironment> Environments,
    bool IsActive,
    string RowVersion) : IVersionedDto;
