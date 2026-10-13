using FluentValidation.TestHelper;
using IncidentHub.Application.Incidents.UpdateIncident;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Common;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.UnitTests.Incidents.UpdateIncident;

public sealed class UpdateIncidentValidatorTests
{
    private readonly UpdateIncidentValidator _validator = new();

    private static UpdateIncidentCommand Command(
        Optional<string?> title = default,
        Optional<string?> description = default,
        Optional<string?> steps = default,
        Optional<Severity?> severity = default,
        Optional<Priority?> priority = default,
        Optional<Guid?> applicationId = default,
        Optional<AppEnvironment?> environment = default,
        string? reason = null) =>
        new(Guid.NewGuid(), title, description, steps, severity, priority, applicationId, environment, reason, [1, 2, 3]);

    private static Optional<T> Set<T>(T value) => new(true, value);

    [Fact]
    public void Validate_EmptyPatch_HasError()
    {
        var result = _validator.TestValidate(Command());

        result.ShouldHaveValidationErrorFor("body");
    }

    [Fact]
    public void Validate_TitleTooShort_HasTitleError()
    {
        var result = _validator.TestValidate(Command(title: Set<string?>("abc")));

        result.ShouldHaveValidationErrorFor("title");
    }

    [Fact]
    public void Validate_NullTitle_HasTitleError()
    {
        var result = _validator.TestValidate(Command(title: Set<string?>(null)));

        result.ShouldHaveValidationErrorFor("title");
    }

    [Fact]
    public void Validate_SeverityWithoutReason_HasReasonError()
    {
        var result = _validator.TestValidate(Command(severity: Set<Severity?>(Severity.Critical)));

        result.ShouldHaveValidationErrorFor("reason");
    }

    [Fact]
    public void Validate_ReasonWithoutSeverity_NoErrors()
    {
        var result = _validator.TestValidate(Command(title: Set<string?>("A new title"), reason: ""));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_DescriptionTooLong_HasDescriptionError()
    {
        var result = _validator.TestValidate(Command(description: Set<string?>(new string('x', Incident.DescriptionMaxLength + 1))));

        result.ShouldHaveValidationErrorFor("description");
    }

    [Fact]
    public void Validate_UndefinedPriority_HasPriorityError()
    {
        var result = _validator.TestValidate(Command(priority: Set<Priority?>((Priority)99)));

        result.ShouldHaveValidationErrorFor("priority");
    }

    [Fact]
    public void Validate_NullPriorityAndSteps_NoErrors()
    {
        var result = _validator.TestValidate(Command(steps: Set<string?>(null), priority: Set<Priority?>(null)));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NullApplicationId_HasApplicationIdError()
    {
        var result = _validator.TestValidate(Command(applicationId: Set<Guid?>(null)));

        result.ShouldHaveValidationErrorFor("applicationId");
    }

    [Fact]
    public void Validate_ValidPatch_NoErrors()
    {
        var result = _validator.TestValidate(Command(
            title: Set<string?>("A better title"),
            description: Set<string?>("<p>Updated.</p>"),
            severity: Set<Severity?>(Severity.High),
            priority: Set<Priority?>(Priority.P2),
            applicationId: Set<Guid?>(Guid.NewGuid()),
            environment: Set<AppEnvironment?>(AppEnvironment.UAT),
            reason: "Impact is wider than first thought"));

        result.ShouldNotHaveAnyValidationErrors();
    }
}
