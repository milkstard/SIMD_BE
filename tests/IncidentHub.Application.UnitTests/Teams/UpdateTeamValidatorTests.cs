using FluentValidation.TestHelper;
using IncidentHub.Application.Teams.UpdateTeam;

namespace IncidentHub.Application.UnitTests.Teams;

public sealed class UpdateTeamValidatorTests
{
    private readonly UpdateTeamValidator _validator = new();

    private static UpdateTeamCommand Command(string? name = "Payments", string? email = "payments@acme.com", string? url = null) =>
        new(Guid.NewGuid(), name!, email!, url, [1, 2, 3]);

    [Fact]
    public void Validate_ValidInput_Passes() =>
        _validator.TestValidate(Command(url: "https://teams.example.com/hook")).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Validate_BlankName_Fails() =>
        _validator.TestValidate(Command(name: " ")).ShouldHaveValidationErrorFor("Name");

    [Fact]
    public void Validate_InvalidEmail_Fails() =>
        _validator.TestValidate(Command(email: "nope")).ShouldHaveValidationErrorFor("Email");

    [Fact]
    public void Validate_HttpUrl_Fails() =>
        _validator.TestValidate(Command(url: "http://teams.example.com/hook")).ShouldHaveValidationErrorFor("TeamsChannelUrl");
}
