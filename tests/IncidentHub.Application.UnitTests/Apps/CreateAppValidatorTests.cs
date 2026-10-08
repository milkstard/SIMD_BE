using FluentValidation.TestHelper;
using IncidentHub.Application.Apps.CreateApp;
using IncidentHub.Domain.Apps;

namespace IncidentHub.Application.UnitTests.Apps;

public sealed class CreateAppValidatorTests
{
    private readonly CreateAppValidator _validator = new();

    private static CreateAppCommand Command(
        string? name = "Checkout",
        string? code = "CHECKOUT",
        Guid? team = null,
        Guid? contact = null,
        IReadOnlyList<AppEnvironment>? environments = null) =>
        new(name!, code!, team ?? Guid.NewGuid(), contact, environments ?? [AppEnvironment.Production, AppEnvironment.UAT]);

    [Fact]
    public void Validate_ValidInput_Passes() =>
        _validator.TestValidate(Command(contact: Guid.NewGuid())).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Validate_LowercaseCode_Passes() =>
        _validator.TestValidate(Command(code: "check-out_1")).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("has space")]
    [InlineData("bad!")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    public void Validate_InvalidCode_Fails(string code) =>
        _validator.TestValidate(Command(code: code)).ShouldHaveValidationErrorFor("Code");

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_BlankName_Fails(string name) =>
        _validator.TestValidate(Command(name: name)).ShouldHaveValidationErrorFor("Name");

    [Fact]
    public void Validate_NameTooLong_Fails() =>
        _validator.TestValidate(Command(name: new string('a', 201))).ShouldHaveValidationErrorFor("Name");

    [Fact]
    public void Validate_EmptyEnvironments_Fails() =>
        _validator.TestValidate(Command(environments: [])).ShouldHaveValidationErrorFor("Environments");

    [Fact]
    public void Validate_DuplicateEnvironments_Fails() =>
        _validator.TestValidate(Command(environments: [AppEnvironment.UAT, AppEnvironment.UAT]))
            .ShouldHaveValidationErrorFor("Environments");

    [Fact]
    public void Validate_UndefinedEnvironment_Fails() =>
        _validator.TestValidate(Command(environments: [(AppEnvironment)99])).ShouldHaveValidationErrorFor("Environments");

    [Fact]
    public void Validate_EmptyOwningTeam_Fails() =>
        _validator.TestValidate(Command(team: Guid.Empty)).ShouldHaveValidationErrorFor("OwningTeamId");

    [Fact]
    public void Validate_EmptyEscalationContactGuid_Fails() =>
        _validator.TestValidate(Command(contact: Guid.Empty)).ShouldHaveValidationErrorFor("EscalationContactId");
}
