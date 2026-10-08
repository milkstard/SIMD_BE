using IncidentHub.Domain.Apps;
using MediatR;

namespace IncidentHub.Application.Apps.CreateApp;

public sealed record CreateAppCommand(
    string Name,
    string Code,
    Guid OwningTeamId,
    Guid? EscalationContactId,
    IReadOnlyList<AppEnvironment> Environments,
    bool IsActive = true) : IRequest<ApplicationDto>;
