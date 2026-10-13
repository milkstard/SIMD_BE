using IncidentHub.Application.Incidents.Dtos;
using MediatR;

namespace IncidentHub.Application.Incidents.AddComment;

public sealed record AddCommentCommand(Guid IncidentId, string Body, bool IsInternal) : IRequest<CommentDto>;
