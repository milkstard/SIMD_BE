using FluentAssertions;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Domain.UnitTests.Incidents;

public sealed class AttachmentTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static Attachment NewAttachment(long sizeBytes = 1024, string fileName = "screenshot.png", string blobPath = "attachments/2026/10/a") =>
        Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), fileName, "image/png", sizeBytes, blobPath, At);

    [Fact]
    public void Create_ValidValues_IsPendingAndUnlinked()
    {
        var uploader = Guid.NewGuid();
        var id = Guid.NewGuid();

        var attachment = Attachment.Create(id, uploader, "screenshot.png", "image/png", 1024, "attachments/2026/10/x", At);

        attachment.Id.Should().Be(id);
        attachment.UploadedById.Should().Be(uploader);
        attachment.ScanStatus.Should().Be(ScanStatus.Pending);
        attachment.IncidentId.Should().BeNull();
        attachment.CommentId.Should().BeNull();
        attachment.CreatedAt.Should().Be(At);
        attachment.SizeBytes.Should().Be(1024);
    }

    [Fact]
    public void Create_ExactlyMaxSize_Succeeds()
    {
        var act = () => NewAttachment(Attachment.MaxSizeBytes);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(Attachment.MaxSizeBytes + 1)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_SizeOutsideRange_Throws(long size)
    {
        var act = () => NewAttachment(size);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankFileName_Throws(string fileName)
    {
        var act = () => NewAttachment(fileName: fileName);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_FileNameOverLimit_Throws()
    {
        var act = () => NewAttachment(fileName: new string('a', Attachment.FileNameMaxLength + 1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_BlobPathOverLimit_Throws()
    {
        var act = () => NewAttachment(blobPath: new string('p', Attachment.BlobPathMaxLength + 1));

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(ScanStatus.Clean)]
    [InlineData(ScanStatus.Infected)]
    public void MarkScanned_FromPending_SetsResult(ScanStatus result)
    {
        var attachment = NewAttachment();

        attachment.MarkScanned(result);

        attachment.ScanStatus.Should().Be(result);
    }

    [Fact]
    public void MarkScanned_FromCleanToInfected_Throws()
    {
        var attachment = NewAttachment();
        attachment.MarkScanned(ScanStatus.Clean);

        var act = () => attachment.MarkScanned(ScanStatus.Infected);

        act.Should().Throw<InvalidOperationException>();
        attachment.ScanStatus.Should().Be(ScanStatus.Clean);
    }

    [Fact]
    public void MarkScanned_FromInfectedToClean_Throws()
    {
        var attachment = NewAttachment();
        attachment.MarkScanned(ScanStatus.Infected);

        var act = () => attachment.MarkScanned(ScanStatus.Clean);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkScanned_SameResultTwice_IsNoOp()
    {
        var attachment = NewAttachment();
        attachment.MarkScanned(ScanStatus.Clean);

        var act = () => attachment.MarkScanned(ScanStatus.Clean);

        act.Should().NotThrow();
        attachment.ScanStatus.Should().Be(ScanStatus.Clean);
    }

    [Fact]
    public void MarkScanned_ToPending_Throws()
    {
        var attachment = NewAttachment();

        var act = () => attachment.MarkScanned(ScanStatus.Pending);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Create_NewAttachment_HasNoScanFailures()
    {
        NewAttachment().ScanAttempts.Should().Be(0);
    }

    [Fact]
    public void RecordScanFailure_WhilePending_CountsAndStaysPending()
    {
        var attachment = NewAttachment();

        attachment.RecordScanFailure();
        attachment.RecordScanFailure();

        attachment.ScanAttempts.Should().Be(2);
        attachment.ScanStatus.Should().Be(ScanStatus.Pending);
    }

    [Theory]
    [InlineData(ScanStatus.Clean)]
    [InlineData(ScanStatus.Infected)]
    public void RecordScanFailure_AfterScanned_Throws(ScanStatus result)
    {
        var attachment = NewAttachment();
        attachment.MarkScanned(result);

        var act = attachment.RecordScanFailure;

        act.Should().Throw<InvalidOperationException>();
        attachment.ScanAttempts.Should().Be(0);
    }
}
