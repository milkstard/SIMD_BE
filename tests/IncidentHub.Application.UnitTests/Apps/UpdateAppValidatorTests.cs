using FluentValidation.TestHelper;
using IncidentHub.Application.Apps.UpdateApp;
using IncidentHub.Domain.Apps;

namespace IncidentHub.Application.UnitTests.Apps;

public sealed class UpdateAppValidatorTests
{
    private readonly UpdateAppValidator _validator = new();

    private static UpdateAppCommand Command(
        string? code = "CHECKOUT",
        IReadOnlyList<AppEnvironment>? environments = null,
        Guid? team = null) =>
        new(
            Guid.NewGuid(),
            "Checkout",
            code!,
            team ?? Guid.NewGuid(),
            null,
            environments ?? [AppEnvironment.Production],
            true,
            [1, 2, 3]);

    [Fact]
    public void Validate_ValidInput_Passes() =>
        _validator.TestValidate(Command()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Validate_InvalidCode_Fails() =>
        _validator.TestValidate(Command(code: "no spaces allowed")).ShouldHaveValidationErrorFor("Code");

    [Fact]
    public void Validate_EmptyEnvironments_Fails() =>
        _validator.TestValidate(Command(environments: [])).ShouldHaveValidationErrorFor("Environments");

    [Fact]
    public void Validate_DuplicateEnvironments_Fails() =>
        _validator.TestValidate(Command(environments: [AppEnvironment.Development, AppEnvironment.Development]))
            .ShouldHaveValidationErrorFor("Environments");

    [Fact]
    public void Validate_EmptyOwningTeam_Fails() =>
        _validator.TestValidate(Command(team: Guid.Empty)).ShouldHaveValidationErrorFor("OwningTeamId");
}
