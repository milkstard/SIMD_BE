using FluentValidation;
using FluentValidation.Results;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Incidents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Incidents.AddComment;

public sealed class AddCommentHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IAppTeamReader teams,
    IHtmlSanitizer sanitizer,
    TimeProvider time,
    ILogger<AddCommentHandler> logger) : IRequestHandler<AddCommentCommand, CommentDto>
{
    public async Task<CommentDto> Handle(AddCommentCommand request, CancellationToken cancellationToken)
    {
        var incident = await IncidentWriteGuard.LoadVisibleAsync(db, teams, currentUser, request.IncidentId, cancellationToken);
        var actor = currentUser.ToActor();

        // 403 wins over a 422 about the body.
        if (request.IsInternal && !IncidentPermissions.IsResponder(actor))
        {
            throw new CommentNotPermittedException();
        }

        var body = sanitizer.Sanitize(request.Body);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ValidationException([new ValidationFailure("body", "Body is required.")]);
        }

        // Sanitizing can lengthen the text (a literal < becomes &lt;), so the stored size is checked again.
        if (body.Length > Comment.BodyMaxLength)
        {
            throw new ValidationException(
                [new ValidationFailure("body", $"Body must be at most {Comment.BodyMaxLength} characters after sanitizing.")]);
        }

        var comment = incident.AddComment(body, request.IsInternal, actor, time.GetUtcNow());
        db.Comments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Comment {CommentId} added to incident {IncidentNumber}",
            comment.Id,
            new IncidentNumber(incident.Number));

        return new CommentDto(
            comment.Id,
            comment.IncidentId,
            new UserRefDto(currentUser.UserId, currentUser.DisplayName, currentUser.Email),
            comment.Body,
            comment.IsInternal,
            [],
            comment.CreatedAt,
            comment.EditedAt);
    }
}
