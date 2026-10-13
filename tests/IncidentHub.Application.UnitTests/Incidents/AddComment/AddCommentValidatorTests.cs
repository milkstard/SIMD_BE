using FluentValidation.TestHelper;
using IncidentHub.Application.Incidents.AddComment;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.UnitTests.Incidents.AddComment;

public sealed class AddCommentValidatorTests
{
    private readonly AddCommentValidator _validator = new();

    private static AddCommentCommand Command(string body, Guid? incidentId = null) =>
        new(incidentId ?? Guid.NewGuid(), body, false);

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
    public void Validate_EmptyIncidentId_HasError()
    {
        _validator.TestValidate(Command("x", Guid.Empty)).ShouldHaveValidationErrorFor(x => x.IncidentId);
    }
}
