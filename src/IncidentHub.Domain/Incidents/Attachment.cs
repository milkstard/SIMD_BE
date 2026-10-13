using IncidentHub.Domain.Common;

namespace IncidentHub.Domain.Incidents;

/// <summary>
/// An uploaded file. It exists unlinked (<see cref="IncidentId"/> null) from the upload until a report or comment links it
/// (spec 08_03). <see cref="BlobPath"/> is internal and never leaves the API.
/// </summary>
public sealed class Attachment : Entity
{
    public const long MaxSizeBytes = 25L * 1024 * 1024;
    public const int FileNameMaxLength = 300;
    public const int ContentTypeMaxLength = 100;
    public const int BlobPathMaxLength = 500;

    /// <summary>Most files one report or comment can link.</summary>
    public const int MaxPerAction = 10;

    private Attachment()
    {
    }

    public Guid? IncidentId { get; private set; }

    public Guid? CommentId { get; private set; }

    public Guid UploadedById { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public string BlobPath { get; private set; } = string.Empty;

    public ScanStatus ScanStatus { get; private set; } = ScanStatus.Pending;

    /// <summary>Failed scan attempts while <c>Pending</c>. The scan job claims the rows with the fewest first, so a file it cannot read never starves newer uploads.</summary>
    public int ScanAttempts { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The id is passed in because the blob path is derived from it before the row exists.</summary>
    public static Attachment Create(
        Guid id,
        Guid uploadedById,
        string fileName,
        string contentType,
        long sizeBytes,
        string blobPath,
        DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > FileNameMaxLength)
        {
            throw new ArgumentException($"File name is required and at most {FileNameMaxLength} characters.", nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > ContentTypeMaxLength)
        {
            throw new ArgumentException($"Content type is required and at most {ContentTypeMaxLength} characters.", nameof(contentType));
        }

        if (sizeBytes <= 0 || sizeBytes > MaxSizeBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, $"Size must be between 1 and {MaxSizeBytes} bytes.");
        }

        if (string.IsNullOrWhiteSpace(blobPath) || blobPath.Length > BlobPathMaxLength)
        {
            throw new ArgumentException($"Blob path is required and at most {BlobPathMaxLength} characters.", nameof(blobPath));
        }

        return new Attachment
        {
            Id = id,
            UploadedById = uploadedById,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            BlobPath = blobPath,
            CreatedAt = at,
        };
    }

    /// <summary>Links the upload to an incident, and to one of its comments when <paramref name="commentId"/> is set. Once only.</summary>
    public void LinkTo(Guid incidentId, Guid? commentId)
    {
        if (IncidentId is not null)
        {
            throw new InvalidOperationException($"Attachment {Id} is already linked.");
        }

        if (ScanStatus == ScanStatus.Infected)
        {
            throw new InvalidOperationException($"Attachment {Id} is infected and cannot be linked.");
        }

        IncidentId = incidentId;
        CommentId = commentId;
    }

    /// <summary>Records the scan outcome. Only <c>Pending</c> can change; repeating the same result is a no-op.</summary>
    public void MarkScanned(ScanStatus result)
    {
        if (result == ScanStatus.Pending)
        {
            throw new InvalidOperationException("A scan result must be Clean or Infected.");
        }

        if (ScanStatus == result)
        {
            return;
        }

        if (ScanStatus != ScanStatus.Pending)
        {
            throw new InvalidOperationException($"Attachment {Id} was already scanned as {ScanStatus}.");
        }

        ScanStatus = result;
    }

    /// <summary>Counts a scan that could not run (blob unreadable, scanner down). The row stays <c>Pending</c> and is retried.</summary>
    public void RecordScanFailure()
    {
        if (ScanStatus != ScanStatus.Pending)
        {
            throw new InvalidOperationException($"Attachment {Id} was already scanned as {ScanStatus}.");
        }

        ScanAttempts++;
    }
}
