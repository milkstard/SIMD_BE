using FluentValidation.TestHelper;
using IncidentHub.Application.Sla.UpdateSlaPolicy;
using IncidentHub.Domain.Incidents.Sla;

namespace IncidentHub.Application.UnitTests.Sla;

public sealed class UpdateSlaPolicyValidatorTests
{
    private readonly UpdateSlaPolicyValidator _validator = new();

    private static UpdateSlaPolicyCommand Command(int ack = 60, int resolve = 480, int percent = 80) =>
        new("High", ack, resolve, percent, [1]);

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        _validator.TestValidate(Command()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_AckNotPositive_Fails(int ack)
    {
        _validator.TestValidate(Command(ack: ack)).ShouldHaveValidationErrorFor(x => x.AckMinutes);
    }

    [Fact]
    public void Validate_ResolveBelowAck_Fails()
    {
        _validator.TestValidate(Command(ack: 60, resolve: 59)).ShouldHaveValidationErrorFor(x => x.ResolveMinutes);
    }

    [Fact]
    public void Validate_ResolveEqualToAck_Passes()
    {
        _validator.TestValidate(Command(ack: 60, resolve: 60)).ShouldNotHaveValidationErrorFor(x => x.ResolveMinutes);
    }

    [Fact]
    public void Validate_ResolveAboveMaximum_Fails()
    {
        _validator.TestValidate(Command(resolve: SlaPolicySetting.MaxResolveMinutes + 1))
            .ShouldHaveValidationErrorFor(x => x.ResolveMinutes);
    }

    [Fact]
    public void Validate_ResolveAtMaximum_Passes()
    {
        _validator.TestValidate(Command(resolve: SlaPolicySetting.MaxResolveMinutes))
            .ShouldNotHaveValidationErrorFor(x => x.ResolveMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Validate_PercentOutOfRange_Fails(int percent)
    {
        _validator.TestValidate(Command(percent: percent)).ShouldHaveValidationErrorFor(x => x.EscalateAtPercent);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    public void Validate_PercentAtBounds_Passes(int percent)
    {
        _validator.TestValidate(Command(percent: percent)).ShouldNotHaveValidationErrorFor(x => x.EscalateAtPercent);
    }
}
