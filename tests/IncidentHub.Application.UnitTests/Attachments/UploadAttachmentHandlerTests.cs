using FluentAssertions;
using FluentValidation;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Attachments.UploadAttachment;
using IncidentHub.Application.Common.Exceptions;
using IncidentHub.Domain.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace IncidentHub.Application.UnitTests.Attachments;

public sealed class UploadAttachmentHandlerTests
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6];

    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly DbSet<Attachment> _attachments = Substitute.For<DbSet<Attachment>>();
    private readonly IFileStore _files = Substitute.For<IFileStore>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly List<Attachment> _added = [];
    private readonly List<byte[]> _uploaded = [];

    public UploadAttachmentHandlerTests()
    {
        _db.Attachments.Returns(_attachments);
        _attachments.When(a => a.Add(Arg.Any<Attachment>())).Do(call => _added.Add(call.Arg<Attachment>()));
        _user.UserId.Returns(_userId);
        _user.DisplayName.Returns("Test User");
        _user.Email.Returns("test@test.local");

        // A real store reads the stream to the end; so does this one, which is what makes the handler count the bytes.
        _files.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                using var copy = new MemoryStream();
                await call.Arg<Stream>().CopyToAsync(copy);
                _uploaded.Add(copy.ToArray());
            });
    }

    private UploadAttachmentHandler Handler() =>
        new(_db, _files, _user, TimeProvider.System, NullLogger<UploadAttachmentHandler>.Instance);

    private static UploadAttachmentCommand Command(byte[] content, string fileName = "shot.png", string contentType = "image/png") =>
        new(fileName, contentType, new MemoryStream(content));

    [Fact]
    public async Task Handle_ValidFile_UploadsAndStoresPending()
    {
        var result = await Handler().Handle(Command(PngHeader), CancellationToken.None);

        _added.Should().ContainSingle();
        var attachment = _added[0];
        attachment.ScanStatus.Should().Be(ScanStatus.Pending);
        attachment.UploadedById.Should().Be(_userId);
        attachment.SizeBytes.Should().Be(PngHeader.Length);
        attachment.BlobPath.Should().StartWith("attachments/").And.EndWith(attachment.Id.ToString("N"));
        _uploaded.Should().ContainSingle().Which.Should().Equal(PngHeader);
        await _files.Received(1).UploadAsync(attachment.BlobPath, Arg.Any<Stream>(), "image/png", Arg.Any<CancellationToken>());
        await _db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        result.Id.Should().Be(attachment.Id);
        result.ScanStatus.Should().Be(ScanStatus.Pending);
        result.UploadedBy.Id.Should().Be(_userId);
        result.SizeBytes.Should().Be(PngHeader.Length);
    }

    [Fact]
    public async Task Handle_FileNameWithDirectories_StoresOnlyTheName()
    {
        await Handler().Handle(Command(PngHeader, fileName: "..\\..\\evil\\shot.png"), CancellationToken.None);

        _added.Should().ContainSingle().Which.FileName.Should().Be("shot.png");
    }

    [Fact]
    public async Task Handle_ContentTypeWithParameters_StoresTheMediaTypeOnly()
    {
        var text = "hello"u8.ToArray();

        await Handler().Handle(Command(text, "app.log", "text/plain; charset=utf-8"), CancellationToken.None);

        _added.Should().ContainSingle().Which.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task Handle_MagicNumberMismatch_Throws()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 };

        var act = () => Handler().Handle(Command(jpeg), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "file");
        await _files.DidNotReceiveWithAnyArgs().UploadAsync(default!, default!, default!, default);
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_EmptyFile_Throws()
    {
        var act = () => Handler().Handle(Command([]), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "file");
        await _files.DidNotReceiveWithAnyArgs().UploadAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Handle_InsertFails_DeletesBlob()
    {
        _db.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));

        var act = () => Handler().Handle(Command(PngHeader), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("db down");
        await _files.Received(1).DeleteAsync(Arg.Is<string>(p => p.StartsWith("attachments/")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UploadFails_StoresNoRow()
    {
        _files.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("storage down"));

        var act = () => Handler().Handle(Command(PngHeader), CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        await _db.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CleanupFails_StillSurfacesTheOriginalError()
    {
        _db.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));
        _files.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("cannot delete"));

        var act = () => Handler().Handle(Command(PngHeader), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("db down");
    }

    [Fact]
    public async Task Handle_OverMaxSize_ThrowsPayloadTooLargeAndDeletesBlob()
    {
        var content = new FillerStream(Attachment.MaxSizeBytes + 1);

        var act = () => Handler().Handle(new UploadAttachmentCommand("big.log", "text/plain", content), CancellationToken.None);

        await act.Should().ThrowAsync<PayloadTooLargeException>();
        await _files.Received(1).DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _added.Should().BeEmpty();

        // Stopped at the limit instead of reading the rest of the body.
        content.BytesServed.Should().BeLessThan(Attachment.MaxSizeBytes + 1 + 1024 * 1024);
    }

    [Fact]
    public async Task Handle_ExactlyMaxSize_Succeeds()
    {
        var content = new FillerStream(Attachment.MaxSizeBytes);

        var result = await Handler().Handle(new UploadAttachmentCommand("big.log", "text/plain", content), CancellationToken.None);

        result.SizeBytes.Should().Be(Attachment.MaxSizeBytes);
    }

    /// <summary>Readable stream of <c>length</c> zero bytes that remembers how much was read from it.</summary>
    private sealed class FillerStream(long length) : Stream
    {
        public long BytesServed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => BytesServed;
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = (int)Math.Min(buffer.Length, length - BytesServed);
            buffer.Span[..count].Clear();
            BytesServed += count;
            return ValueTask.FromResult(count);
        }

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
