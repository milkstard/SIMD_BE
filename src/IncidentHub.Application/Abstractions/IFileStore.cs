namespace IncidentHub.Application.Abstractions;

/// <summary>Blob storage for attachment content. Paths are server-generated and never exposed to clients.</summary>
public interface IFileStore
{
    /// <summary>Streams <paramref name="content"/> to <paramref name="path"/> without buffering the whole file.</summary>
    Task UploadAsync(string path, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>Deletes the blob; a missing blob is not an error.</summary>
    Task DeleteAsync(string path, CancellationToken cancellationToken);

    /// <summary>A read-only link that expires after <paramref name="lifetime"/> and downloads the blob as <paramref name="fileName"/>.</summary>
    Task<Uri> CreateDownloadUriAsync(string path, string fileName, string contentType, TimeSpan lifetime, CancellationToken cancellationToken);

    /// <summary>Opens the blob for reading; used by the scanner only, never to serve a client.</summary>
    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);
}
