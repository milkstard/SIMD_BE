using MediatR;

namespace IncidentHub.Application.Sla.UpdateSlaPolicy;

/// <param name="Id">The severity name.</param>
/// <param name="ExpectedRowVersion">The decoded <c>If-Match</c> value the client last saw.</param>
public sealed record UpdateSlaPolicyCommand(
    string Id,
    int AckMinutes,
    int ResolveMinutes,
    int EscalateAtPercent,
    byte[] ExpectedRowVersion) : IRequest<SlaPolicyDto>;
