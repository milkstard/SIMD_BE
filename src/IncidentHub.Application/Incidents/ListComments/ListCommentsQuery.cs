using IncidentHub.Application.Common.Paging;
using IncidentHub.Application.Incidents.Dtos;
using MediatR;

namespace IncidentHub.Application.Incidents.ListComments;

public sealed record ListCommentsQuery(Guid IncidentId, string? Cursor, int Limit = PageLimits.Default, bool IncludeTotal = false)
    : IRequest<Paged<CommentDto>>;
