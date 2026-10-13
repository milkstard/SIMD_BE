using FluentValidation;
using FluentValidation.Results;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Attachments;
using IncidentHub.Application.Common;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Incidents;
using MediatR;
using Microsoft.EntityFrameworkCore;
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

        var attachments = await AttachmentLinker.LoadLinkableAsync(db, request.AttachmentIds, currentUser.UserId, cancellationToken);

        var comment = incident.AddComment(body, request.IsInternal, actor, time.GetUtcNow(), attachments);
        db.Comments.Add(comment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) when (attachments.Count > 0)
        {
            // Lost the race for one of the files (UPDATE ... WHERE IncidentId IS NULL); nothing was saved.
            throw AttachmentLinker.Unavailable422();
        }

        logger.LogInformation(
            "Comment {CommentId} added to incident {IncidentNumber}",
            comment.Id,
            new IncidentNumber(incident.Number));

        var author = new UserRefDto(currentUser.UserId, currentUser.DisplayName, currentUser.Email);
        return new CommentDto(
            comment.Id,
            comment.IncidentId,
            author,
            comment.Body,
            comment.IsInternal,
            attachments.Select(a => AttachmentDto.From(a, author)).ToList(),
            comment.CreatedAt,
            comment.EditedAt);
    }
}
