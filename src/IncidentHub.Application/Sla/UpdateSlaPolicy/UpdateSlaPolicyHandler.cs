using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Sla.UpdateSlaPolicy;

public sealed class UpdateSlaPolicyHandler(IAppDbContext db, ILogger<UpdateSlaPolicyHandler> logger)
    : IRequestHandler<UpdateSlaPolicyCommand, SlaPolicyDto>
{
    public async Task<SlaPolicyDto> Handle(UpdateSlaPolicyCommand request, CancellationToken cancellationToken)
    {
        if (!SlaPolicyIds.TryParse(request.Id, out var severity))
        {
            throw new NotFoundException($"SLA policy {request.Id} was not found.");
        }

        var setting = await db.SlaPolicies.FirstOrDefaultAsync(p => p.Severity == severity, cancellationToken)
            ?? throw new NotFoundException($"SLA policy {request.Id} was not found.");

        if (!setting.RowVersion.AsSpan().SequenceEqual(request.ExpectedRowVersion))
        {
            throw new ConflictException($"SLA policy {request.Id} was modified by someone else.");
        }

        setting.Update(request.AckMinutes, request.ResolveMinutes, request.EscalateAtPercent);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("SLA policy {Severity} updated", setting.Severity);
        return SlaPolicyDto.From(setting);
    }
}
