using FluentValidation.TestHelper;
using IncidentHub.Application.Incidents.ReportIncident;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.UnitTests.Incidents.ReportIncident;

public sealed class ReportIncidentValidatorTests
{
    private readonly ReportIncidentValidator _validator = new();

    private static ReportIncidentCommand Valid(
        string title = "Checkout fails after payment",
        string description = "<p>Users see an error page.</p>",
        string? steps = "1. Pay",
        AppEnvironment environment = AppEnvironment.Production,
        Severity severity = Severity.High,
        IReadOnlyList<Guid>? attachmentIds = null) =>
        new(Guid.NewGuid(), environment, severity, title, description, steps, attachmentIds ?? []);

    [Fact]
    public void Validate_ValidCommand_NoErrors()
    {
        var result = _validator.TestValidate(Valid());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_TitleTooShort_HasTitleError()
    {
        var result = _validator.TestValidate(Valid(title: "abcd"));

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Validate_TitlePaddedWithSpaces_UsesTrimmedLength()
    {
        var result = _validator.TestValidate(Valid(title: "  abcd  "));

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Validate_TitleTooLong_HasTitleError()
    {
        var result = _validator.TestValidate(Valid(title: new string('a', Incident.TitleMaxLength + 1)));

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankTitle_HasTitleError(string title)
    {
        var result = _validator.TestValidate(Valid(title: title));

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void Validate_DescriptionTooLong_HasDescriptionError()
    {
        var result = _validator.TestValidate(Valid(description: new string('a', Incident.DescriptionMaxLength + 1)));

        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Validate_StepsTooLong_HasStepsError()
    {
        var result = _validator.TestValidate(Valid(steps: new string('a', Incident.StepsMaxLength + 1)));

        result.ShouldHaveValidationErrorFor(x => x.StepsToReproduce);
    }

    [Fact]
    public void Validate_UndefinedSeverity_HasSeverityError()
    {
        var result = _validator.TestValidate(Valid(severity: (Severity)99));

        result.ShouldHaveValidationErrorFor(x => x.Severity);
    }

    [Fact]
    public void Validate_UndefinedEnvironment_HasEnvironmentError()
    {
        var result = _validator.TestValidate(Valid(environment: (AppEnvironment)99));

        result.ShouldHaveValidationErrorFor(x => x.Environment);
    }

    [Fact]
    public void Validate_EmptyApplicationId_HasApplicationIdError()
    {
        var result = _validator.TestValidate(Valid() with { ApplicationId = Guid.Empty });

        result.ShouldHaveValidationErrorFor(x => x.ApplicationId);
    }

    [Fact]
    public void Validate_AttachmentIdsNotEmpty_HasAttachmentIdsError()
    {
        var result = _validator.TestValidate(Valid(attachmentIds: [Guid.NewGuid()]));

        result.ShouldHaveValidationErrorFor(x => x.AttachmentIds).WithErrorMessage("Attachments cannot be linked yet.");
    }
}
