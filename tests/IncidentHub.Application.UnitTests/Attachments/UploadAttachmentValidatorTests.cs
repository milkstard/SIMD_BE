using FluentValidation.TestHelper;
using IncidentHub.Application.Attachments.UploadAttachment;

namespace IncidentHub.Application.UnitTests.Attachments;

public sealed class UploadAttachmentValidatorTests
{
    private readonly UploadAttachmentValidator _validator = new();

    private static UploadAttachmentCommand Command(string fileName, string contentType) =>
        new(fileName, contentType, Stream.Null);

    [Theory]
    [InlineData("shot.png", "image/png")]
    [InlineData("server.log", "text/plain")]
    [InlineData("data.json", "application/json")]
    public void Validate_AllowedFile_NoErrors(string fileName, string contentType)
    {
        _validator.TestValidate(Command(fileName, contentType)).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("setup.exe", "application/octet-stream")]
    [InlineData("logo.svg", "image/svg+xml")]
    [InlineData("doc.pdf", "application/pdf")]
    public void Validate_DisallowedType_HasError(string fileName, string contentType)
    {
        _validator.TestValidate(Command(fileName, contentType)).ShouldHaveValidationErrorFor("file");
    }

    [Theory]
    [InlineData("shot.png", "text/plain")]
    [InlineData("data.json", "image/png")]
    [InlineData("setup.exe", "image/png")]
    public void Validate_TypeExtensionMismatch_HasError(string fileName, string contentType)
    {
        _validator.TestValidate(Command(fileName, contentType)).ShouldHaveValidationErrorFor("file");
    }

    [Fact]
    public void Validate_EmptyFileName_HasError()
    {
        _validator.TestValidate(Command(string.Empty, "image/png")).ShouldHaveValidationErrorFor("file");
    }
}
