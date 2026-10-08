using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Apps.UpdateApp;

public sealed class UpdateAppHandler(IAppDbContext db, ICurrentUser user, ILogger<UpdateAppHandler> logger)
    : IRequestHandler<UpdateAppCommand, ApplicationDto>
{
    public async Task<ApplicationDto> Handle(UpdateAppCommand request, CancellationToken cancellationToken)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException($"Application {request.Id} was not found.");

        if (!app.RowVersion.AsSpan().SequenceEqual(request.ExpectedRowVersion))
        {
            throw new ConflictException($"Application {request.Id} was modified by someone else.");
        }

        // Moving an application to another team changes who can see and act on its incidents: Admin only.
        if (request.OwningTeamId != app.OwningTeamId && !user.IsInRole(UserRole.Admin))
        {
            throw new ForbiddenException("Only an Admin can change the owning team of an application.");
        }

        await AppReferenceChecks.EnsureReferencesExistAsync(db, request.OwningTeamId, request.EscalationContactId, cancellationToken);

        if (await AppReferenceChecks.CodeTakenAsync(db, request.Code, request.Id, cancellationToken))
        {
            throw new ConflictException($"An application with code {request.Code.ToUpperInvariant()} already exists.");
        }

        app.Update(request.Name, request.Code, request.OwningTeamId, request.EscalationContactId, request.Environments, request.IsActive);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            throw new ConflictException($"An application with code {app.Code} already exists.");
        }

        logger.LogInformation("Application {Code} updated", app.Code);
        return await AppQueries.Project(db, db.Applications.AsNoTracking().Where(a => a.Id == app.Id)).FirstAsync(cancellationToken);
    }
}
