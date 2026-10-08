using FluentValidation.TestHelper;
using IncidentHub.Application.Teams.CreateTeam;

namespace IncidentHub.Application.UnitTests.Teams;

public sealed class CreateTeamValidatorTests
{
    private readonly CreateTeamValidator _validator = new();

    [Fact]
    public void Validate_ValidInput_Passes()
    {
        var result = _validator.TestValidate(new CreateTeamCommand("Payments", "payments@acme.com", "https://teams.example.com/hook"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NullChannelUrl_Passes()
    {
        var result = _validator.TestValidate(new CreateTeamCommand("Payments", "payments@acme.com", null));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_BlankName_Fails(string? name)
    {
        var result = _validator.TestValidate(new CreateTeamCommand(name!, "payments@acme.com", null));

        result.ShouldHaveValidationErrorFor("Name");
    }

    [Fact]
    public void Validate_NameTooLong_Fails()
    {
        var result = _validator.TestValidate(new CreateTeamCommand(new string('a', 201), "payments@acme.com", null));

        result.ShouldHaveValidationErrorFor("Name");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_InvalidEmail_Fails(string? email)
    {
        var result = _validator.TestValidate(new CreateTeamCommand("Payments", email!, null));

        result.ShouldHaveValidationErrorFor("Email");
    }

    [Theory]
    [InlineData("http://teams.example.com/hook")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public void Validate_NonHttpsChannelUrl_Fails(string url)
    {
        var result = _validator.TestValidate(new CreateTeamCommand("Payments", "payments@acme.com", url));

        result.ShouldHaveValidationErrorFor("TeamsChannelUrl");
    }
}
