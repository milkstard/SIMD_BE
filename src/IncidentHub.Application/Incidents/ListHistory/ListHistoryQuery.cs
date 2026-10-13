using IncidentHub.Application.Common.Paging;
using IncidentHub.Application.Incidents.Dtos;
using MediatR;

namespace IncidentHub.Application.Incidents.ListHistory;

public sealed record ListHistoryQuery(Guid IncidentId, string? Cursor, int Limit = PageLimits.Default, bool IncludeTotal = false)
    : IRequest<Paged<HistoryEntryDto>>;
