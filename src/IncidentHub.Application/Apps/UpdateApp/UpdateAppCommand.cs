using IncidentHub.Domain.Apps;
using MediatR;

namespace IncidentHub.Application.Apps.UpdateApp;

/// <param name="ExpectedRowVersion">The decoded <c>If-Match</c> value the client last saw.</param>
public sealed record UpdateAppCommand(
    Guid Id,
    string Name,
    string Code,
    Guid OwningTeamId,
    Guid? EscalationContactId,
    IReadOnlyList<AppEnvironment> Environments,
    bool IsActive,
    byte[] ExpectedRowVersion) : IRequest<ApplicationDto>;
