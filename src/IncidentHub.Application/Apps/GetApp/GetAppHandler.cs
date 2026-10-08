using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Apps.GetApp;

public sealed class GetAppHandler(IAppDbContext db, ICurrentUser user) : IRequestHandler<GetAppQuery, ApplicationDto>
{
    public async Task<ApplicationDto> Handle(GetAppQuery request, CancellationToken cancellationToken)
    {
        var visible = AppQueries.VisibleTo(db.Applications.AsNoTracking().Where(a => a.Id == request.Id), db, user);

        // Missing and not-visible are indistinguishable on purpose (existence hiding).
        return await AppQueries.Project(db, visible).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Application {request.Id} was not found.");
    }
}
