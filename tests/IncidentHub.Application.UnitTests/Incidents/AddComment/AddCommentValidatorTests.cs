using FluentValidation.TestHelper;
using IncidentHub.Application.Incidents.AddComment;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.UnitTests.Incidents.AddComment;

public sealed class AddCommentValidatorTests
{
    private readonly AddCommentValidator _validator = new();

    private static AddCommentCommand Command(string body, Guid? incidentId = null, IReadOnlyList<Guid>? attachmentIds = null) =>
        new(incidentId ?? Guid.NewGuid(), body, false, attachmentIds ?? []);

    [Fact]
    public void Validate_ValidBody_NoErrors()
    {
        _validator.TestValidate(Command("<p>Looking into it</p>")).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyBody_HasError(string body)
    {
        _validator.TestValidate(Command(body)).ShouldHaveValidationErrorFor(x => x.Body);
    }

    [Fact]
    public void Validate_BodyTooLong_HasError()
    {
        _validator.TestValidate(Command(new string('a', Comment.BodyMaxLength + 1))).ShouldHaveValidationErrorFor(x => x.Body);
    }

    [Fact]
    public void Validate_BodyAtLimit_NoErrors()
    {
        _validator.TestValidate(Command(new string('a', Comment.BodyMaxLength))).ShouldNotHaveValidationErrorFor(x => x.Body);
    }

    [Fact]
    public void Validate_ElevenAttachmentIds_HasError()
    {
        var ids = Enumerable.Range(0, Attachment.MaxPerAction + 1).Select(_ => Guid.NewGuid()).ToList();

        _validator.TestValidate(Command("x", attachmentIds: ids)).ShouldHaveValidationErrorFor(x => x.AttachmentIds);
    }

    [Fact]
    public void Validate_DuplicateAttachmentIds_HasError()
    {
        var id = Guid.NewGuid();

        _validator.TestValidate(Command("x", attachmentIds: [id, id])).ShouldHaveValidationErrorFor(x => x.AttachmentIds);
    }

    [Fact]
    public void Validate_EmptyAttachmentId_HasError()
    {
        _validator.TestValidate(Command("x", attachmentIds: [Guid.Empty])).ShouldHaveValidationErrorFor(x => x.AttachmentIds);
    }

    [Fact]
    public void Validate_TenAttachmentIds_NoErrors()
    {
        var ids = Enumerable.Range(0, Attachment.MaxPerAction).Select(_ => Guid.NewGuid()).ToList();

        _validator.TestValidate(Command("x", attachmentIds: ids)).ShouldNotHaveValidationErrorFor(x => x.AttachmentIds);
    }

    [Fact]
    public void Validate_EmptyIncidentId_HasError()
    {
        _validator.TestValidate(Command("x", Guid.Empty)).ShouldHaveValidationErrorFor(x => x.IncidentId);
    }
}
