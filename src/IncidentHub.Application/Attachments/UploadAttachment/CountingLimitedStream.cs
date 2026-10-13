using IncidentHub.Application.Common.Exceptions;

namespace IncidentHub.Application.Attachments.UploadAttachment;

/// <summary>
/// Read-only stream that replays an already-read header, then the rest of the source, and counts what it hands out.
/// Reading past <paramref name="limit"/> throws <see cref="PayloadTooLargeException"/>, so an oversized upload stops
/// as soon as the limit is crossed instead of after the whole body arrived.
/// </summary>
internal sealed class CountingLimitedStream(ReadOnlyMemory<byte> header, Stream source, long limit) : Stream
{
    private int _headerRead;

    public long BytesRead { get; private set; }

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
        int read;
        if (_headerRead < header.Length)
        {
            read = Math.Min(buffer.Length, header.Length - _headerRead);
            header.Slice(_headerRead, read).CopyTo(buffer);
            _headerRead += read;
        }
        else
        {
            read = await source.ReadAsync(buffer, cancellationToken);
        }

        BytesRead += read;
        if (BytesRead > limit)
        {
            throw new PayloadTooLargeException($"File is larger than the {limit / (1024 * 1024)} MB limit.");
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
