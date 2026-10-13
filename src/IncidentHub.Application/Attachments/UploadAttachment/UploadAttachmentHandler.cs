using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Common;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Application.Incidents.Dtos;
using IncidentHub.Domain.Incidents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IncidentHub.Application.Attachments.UploadAttachment;

public sealed class UploadAttachmentHandler(
    IAppDbContext db,
    IFileStore files,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<UploadAttachmentHandler> logger) : IRequestHandler<UploadAttachmentCommand, AttachmentDto>
{
    public async Task<AttachmentDto> Handle(UploadAttachmentCommand request, CancellationToken cancellationToken)
    {
        var header = new byte[AttachmentRules.SignatureLength];
        var headerLength = await ReadHeaderAsync(request.Content, header, cancellationToken);

        if (headerLength == 0)
        {
            throw FileError("File is empty.");
        }

        var contentType = AttachmentRules.NormalizeContentType(request.ContentType);
        if (!AttachmentRules.MatchesSignature(contentType, header.AsSpan(0, headerLength)))
        {
            throw FileError("File content does not match its declared type.");
        }

        var now = time.GetUtcNow();
        var id = Guid.NewGuid();
        var blobPath = $"attachments/{now.ToString("yyyy", CultureInfo.InvariantCulture)}/{now.ToString("MM", CultureInfo.InvariantCulture)}/{id:N}";
        var fileName = AttachmentRules.SanitizeFileName(request.FileName);
        var content = new CountingLimitedStream(header.AsMemory(0, headerLength), request.Content, Attachment.MaxSizeBytes);

        try
        {
            await files.UploadAsync(blobPath, content, contentType, cancellationToken);

            var attachment = Attachment.Create(id, currentUser.UserId, fileName, contentType, content.BytesRead, blobPath, now);
            db.Attachments.Add(attachment);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Attachment {AttachmentId} uploaded ({SizeBytes} bytes)", attachment.Id, attachment.SizeBytes);

            return new AttachmentDto(
                attachment.Id,
                attachment.FileName,
                attachment.ContentType,
                attachment.SizeBytes,
                attachment.ScanStatus,
                new UserRefDto(currentUser.UserId, currentUser.DisplayName, currentUser.Email),
                attachment.CreatedAt);
        }
        catch (Exception exception)
        {
            // Whatever failed, no row points at the blob, so it would be an orphan.
            await DeleteBestEffortAsync(blobPath);

            // The store may wrap what the stream threw; the stream knows whether the limit was crossed.
            if (exception is not PayloadTooLargeException && content.BytesRead > Attachment.MaxSizeBytes)
            {
                throw new PayloadTooLargeException($"File is larger than the {Attachment.MaxSizeBytes / (1024 * 1024)} MB limit.");
            }

            throw;
        }
    }

    private static async Task<int> ReadHeaderAsync(Stream source, byte[] header, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < header.Length)
        {
            var read = await source.ReadAsync(header.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static ValidationException FileError(string message) =>
        new([new ValidationFailure("file", message)]);

    private async Task DeleteBestEffortAsync(string blobPath)
    {
        try
        {
            // The caller may have gone away; the cleanup must not be cancelled with the request.
            await files.DeleteAsync(blobPath, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not delete orphan blob {BlobPath}", blobPath);
        }
    }
}
