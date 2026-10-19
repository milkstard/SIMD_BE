using IncidentHub.Application.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Sla.ListSlaPolicies;

public sealed class ListSlaPoliciesHandler(IAppDbContext db)
    : IRequestHandler<ListSlaPoliciesQuery, IReadOnlyList<SlaPolicyDto>>
{
    public async Task<IReadOnlyList<SlaPolicyDto>> Handle(ListSlaPoliciesQuery request, CancellationToken cancellationToken)
    {
        var settings = await db.SlaPolicies.AsNoTracking().ToListAsync(cancellationToken);

        // Severity is stored as its name, so SQL would order alphabetically; the enum value is Critical, High, Medium, Low.
        return settings.OrderBy(s => s.Severity).Select(SlaPolicyDto.From).ToList();
    }
}
