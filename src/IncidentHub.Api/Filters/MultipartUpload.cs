using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace IncidentHub.Api.Filters;

/// <summary>
/// For an action that reads <c>multipart/form-data</c> itself with <see cref="MultipartFileReader"/>: removes the form value
/// providers, which would otherwise buffer the whole body (up to 25 MB) into memory or a temp file before the action runs.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        context.ValueProviderFactories.RemoveType<FormValueProviderFactory>();
        context.ValueProviderFactories.RemoveType<FormFileValueProviderFactory>();
        context.ValueProviderFactories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}

/// <summary>The single file part of an upload, streamed straight from the request body.</summary>
public sealed class MultipartFile(string fileName, string contentType, Stream content)
{
    public string FileName { get; } = fileName;

    public string ContentType { get; } = contentType;

    public Stream Content { get; } = content;
}

/// <summary>
/// Reads an upload with <see cref="MultipartReader"/>: exactly one part, named <c>file</c>, without buffering it.
/// Everything wrong with the shape of the request is a 400 (<see cref="BadHttpRequestException"/>).
/// </summary>
public static class MultipartFileReader
{
    public const string FieldName = "file";

    private const int MaxBoundaryLength = 70;

    public static async Task<MultipartFile> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadHttpRequestException("The request must be multipart/form-data.", StatusCodes.Status400BadRequest);
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary) || boundary.Length > MaxBoundaryLength)
        {
            throw new BadHttpRequestException("The multipart boundary is missing or too long.", StatusCodes.Status400BadRequest);
        }

        var reader = new MultipartReader(boundary, request.Body);
        var section = await reader.ReadNextSectionAsync(cancellationToken);

        if (section is null
            || !ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
            || !disposition.IsFileDisposition()
            || HeaderUtilities.RemoveQuotes(disposition.Name).Value != FieldName)
        {
            throw new BadHttpRequestException($"A file part named '{FieldName}' is required.", StatusCodes.Status400BadRequest);
        }

        var fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? string.Empty;
        return new MultipartFile(
            fileName,
            section.ContentType ?? "application/octet-stream",
            new SinglePartStream(reader, section.Body));
    }

    /// <summary>
    /// The body of the first part. When it ends, the reader must have nothing more: a second part is a 400, raised while the
    /// upload still runs so the handler discards the half-written blob and stores no row.
    /// </summary>
    private sealed class SinglePartStream(MultipartReader reader, Stream part) : Stream
    {
        private bool _endChecked;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await part.ReadAsync(buffer, cancellationToken);
            if (read == 0 && !_endChecked)
            {
                _endChecked = true;
                if (await reader.ReadNextSectionAsync(cancellationToken) is not null)
                {
                    throw new BadHttpRequestException("Only one file part is allowed.", StatusCodes.Status400BadRequest);
                }
            }

            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
