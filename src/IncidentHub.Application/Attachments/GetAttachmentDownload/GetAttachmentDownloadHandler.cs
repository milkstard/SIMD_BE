using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Application.Incidents;
using IncidentHub.Domain.Incidents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Application.Attachments.GetAttachmentDownload;

public sealed class GetAttachmentDownloadHandler(
    IAppDbContext db,
    IAppTeamReader teams,
    ICurrentUser currentUser,
    IFileStore files) : IRequestHandler<GetAttachmentDownloadQuery, Uri>
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);

    private const string NotFoundMessage = "Attachment not found.";

    public async Task<Uri> Handle(GetAttachmentDownloadQuery request, CancellationToken cancellationToken)
    {
        var attachment = await db.Attachments.AsNoTracking()
            .Where(a => a.Id == request.AttachmentId)
            .Select(a => new
            {
                a.IncidentId,
                a.UploadedById,
                a.ScanStatus,
                a.BlobPath,
                a.FileName,
                a.ContentType,
                ApplicationId = db.Incidents.Where(i => i.Id == a.IncidentId).Select(i => (Guid?)i.ApplicationId).FirstOrDefault(),
                CommentIsInternal = db.Comments.Where(c => c.Id == a.CommentId).Select(c => (bool?)c.IsInternal).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(NotFoundMessage);

        // Visibility first, so a caller who may not see the file learns nothing about its scan status.
        if (attachment.IncidentId is null)
        {
            if (attachment.UploadedById != currentUser.UserId)
            {
                throw new NotFoundException(NotFoundMessage);
            }
        }
        else
        {
            if (attachment.ApplicationId is not { } applicationId
                || !await teams.IsMemberAsync(currentUser.UserId, applicationId, cancellationToken))
            {
                throw new NotFoundException(NotFoundMessage);
            }

            if (attachment.CommentIsInternal == true && !IncidentPermissions.IsResponder(currentUser.ToActor()))
            {
                throw new NotFoundException(NotFoundMessage);
            }
        }

        if (attachment.ScanStatus != ScanStatus.Clean)
        {
            throw new AttachmentNotReadyException(attachment.ScanStatus);
        }

        return await files.CreateDownloadUriAsync(
            attachment.BlobPath,
            attachment.FileName,
            attachment.ContentType,
            LinkLifetime,
            cancellationToken);
    }
}
