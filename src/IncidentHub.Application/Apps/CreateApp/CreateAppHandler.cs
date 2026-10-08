using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Domain.Apps;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Apps.CreateApp;

public sealed class CreateAppHandler(IAppDbContext db, ILogger<CreateAppHandler> logger)
    : IRequestHandler<CreateAppCommand, ApplicationDto>
{
    public async Task<ApplicationDto> Handle(CreateAppCommand request, CancellationToken cancellationToken)
    {
        await AppReferenceChecks.EnsureReferencesExistAsync(db, request.OwningTeamId, request.EscalationContactId, cancellationToken);

        if (await AppReferenceChecks.CodeTakenAsync(db, request.Code, exceptId: null, cancellationToken))
        {
            throw new ConflictException($"An application with code {request.Code.ToUpperInvariant()} already exists.");
        }

        var app = MonitoredApp.Create(request.Name, request.Code, request.OwningTeamId, request.EscalationContactId, request.Environments);
        if (!request.IsActive)
        {
            app.Update(request.Name, request.Code, request.OwningTeamId, request.EscalationContactId, request.Environments, isActive: false);
        }

        db.Applications.Add(app);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            // Lost a race with a concurrent create of the same code; the unique index protects us.
            throw new ConflictException($"An application with code {app.Code} already exists.");
        }

        logger.LogInformation("Application {Code} created", app.Code);
        return await AppQueries.Project(db, db.Applications.AsNoTracking().Where(a => a.Id == app.Id)).FirstAsync(cancellationToken);
    }
}
