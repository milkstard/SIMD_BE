using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Sla.GetSlaPolicy;

public sealed class GetSlaPolicyHandler(IAppDbContext db) : IRequestHandler<GetSlaPolicyQuery, SlaPolicyDto>
{
    public async Task<SlaPolicyDto> Handle(GetSlaPolicyQuery request, CancellationToken cancellationToken)
    {
        if (!SlaPolicyIds.TryParse(request.Id, out var severity))
        {
            throw new NotFoundException($"SLA policy {request.Id} was not found.");
        }

        var setting = await db.SlaPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Severity == severity, cancellationToken)
            ?? throw new NotFoundException($"SLA policy {request.Id} was not found.");

        return SlaPolicyDto.From(setting);
    }
}
