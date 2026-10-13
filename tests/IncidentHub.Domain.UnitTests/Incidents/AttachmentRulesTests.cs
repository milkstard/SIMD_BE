using System.Text;
using FluentAssertions;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Domain.UnitTests.Incidents;

public sealed class AttachmentRulesTests
{
    [Theory]
    [InlineData("a.png", "image/png")]
    [InlineData("a.PNG", "image/png")]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("a.gif", "image/gif")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.txt", "text/plain")]
    [InlineData("server.log", "text/plain")]
    [InlineData("server.log", "text/plain; charset=utf-8")]
    [InlineData("data.json", "application/json")]
    public void IsAllowed_AllowedPair_ReturnsTrue(string fileName, string contentType)
    {
        AttachmentRules.IsAllowed(fileName, contentType).Should().BeTrue();
    }

    [Theory]
    [InlineData("a.svg", "image/svg+xml")]
    [InlineData("a.svg", "image/png")]
    [InlineData("a.png", "image/svg+xml")]
    [InlineData("a.pdf", "application/pdf")]
    [InlineData("a.exe", "application/octet-stream")]
    [InlineData("a.exe", "image/png")]
    [InlineData("a.png", "text/plain")]
    [InlineData("a.json", "text/plain")]
    [InlineData("a.txt", "application/json")]
    [InlineData("a.html", "text/html")]
    [InlineData("noextension", "text/plain")]
    [InlineData("a.png", "")]
    [InlineData("", "image/png")]
    public void IsAllowed_DisallowedOrMismatchedPair_ReturnsFalse(string fileName, string contentType)
    {
        AttachmentRules.IsAllowed(fileName, contentType).Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_DoubleExtensionEndingInExe_ReturnsFalse()
    {
        AttachmentRules.IsAllowed("report.png.exe", "image/png").Should().BeFalse();
    }

    [Theory]
    [InlineData("..\\..\\windows\\evil.png", "evil.png")]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData("C:\\temp\\shot.png", "shot.png")]
    [InlineData("/var/log/app.log", "app.log")]
    [InlineData("sh\"ot.png", "shot.png")]
    [InlineData("  spaced.png  ", "spaced.png")]
    public void SanitizeFileName_PathOrUnsafeCharacters_AreRemoved(string input, string expected)
    {
        AttachmentRules.SanitizeFileName(input).Should().Be(expected);
    }

    [Fact]
    public void SanitizeFileName_BidiAndZeroWidthCharacters_AreRemoved()
    {
        AttachmentRules.SanitizeFileName("report\u202Egnp.exe\u200B.txt\u2028").Should().Be("reportgnp.exe.txt");
    }

    [Fact]
    public void SanitizeFileName_ControlCharacters_AreRemoved()
    {
        AttachmentRules.SanitizeFileName("sho\r\nt\0.png").Should().Be("shot.png");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../")]
    [InlineData("...")]
    public void SanitizeFileName_NothingLeft_FallsBackToFile(string? input)
    {
        AttachmentRules.SanitizeFileName(input).Should().Be("file");
    }

    [Fact]
    public void SanitizeFileName_OverLimit_IsCappedAndKeepsExtension()
    {
        var result = AttachmentRules.SanitizeFileName(new string('a', 500) + ".png");

        result.Should().HaveLength(Attachment.FileNameMaxLength);
        result.Should().EndWith(".png");
    }

    [Fact]
    public void SanitizeFileName_ThenIsAllowed_StillAcceptsTheTraversalName()
    {
        // The directory part is dropped, so the extension that counts is the real one.
        AttachmentRules.IsAllowed("..\\..\\x.png", "image/png").Should().BeTrue();
    }

    [Theory]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }, true)]
    [InlineData("image/png", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 }, false)]
    [InlineData("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 }, true)]
    [InlineData("image/jpeg", new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, false)]
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, true)]
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x41, 0x56, 0x49, 0x20 }, false)]
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46 }, false)]
    [InlineData("text/plain", new byte[] { 0x4D, 0x5A }, true)]
    [InlineData("application/json", new byte[] { 0x7B }, true)]
    public void MatchesSignature_ByDeclaredType(string contentType, byte[] header, bool expected)
    {
        AttachmentRules.MatchesSignature(contentType, header).Should().Be(expected);
    }

    [Theory]
    [InlineData("GIF87a")]
    [InlineData("GIF89a")]
    public void MatchesSignature_Gif_AcceptsBothVersions(string magic)
    {
        AttachmentRules.MatchesSignature("image/gif", Encoding.ASCII.GetBytes(magic + "xxxx")).Should().BeTrue();
    }

    [Fact]
    public void MatchesSignature_GifWithWrongMagic_ReturnsFalse()
    {
        AttachmentRules.MatchesSignature("image/gif", Encoding.ASCII.GetBytes("GIF90a")).Should().BeFalse();
    }

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("IMAGE/JPEG", true)]
    [InlineData("text/plain", false)]
    [InlineData("application/json", false)]
    public void IsImage_ByContentType(string contentType, bool expected)
    {
        AttachmentRules.IsImage(contentType).Should().Be(expected);
    }
}
