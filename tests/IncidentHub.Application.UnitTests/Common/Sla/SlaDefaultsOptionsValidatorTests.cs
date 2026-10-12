using FluentAssertions;
using IncidentHub.Application.Common.Sla;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Application.UnitTests.Common.Sla;

public sealed class SlaDefaultsOptionsValidatorTests
{
    private readonly SlaDefaultsOptionsValidator _validator = new();

    [Fact]
    public void Validate_Defaults_Succeeds()
    {
        var result = _validator.Validate(null, new SlaDefaultsOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_DefaultsConvertToPolicies_ForEverySeverity()
    {
        var options = new SlaDefaultsOptions();

        var critical = options.ToPolicy(Severity.Critical);
        var low = options.ToPolicy(Severity.Low);

        (critical.AckMinutes, critical.ResolveMinutes, critical.EscalateAtPercent).Should().Be((15, 240, 80));
        (low.AckMinutes, low.ResolveMinutes, low.EscalateAtPercent).Should().Be((480, 7_200, 80));
    }

    [Fact]
    public void Validate_ResolveLessThanAck_FailsNamingField()
    {
        var options = new SlaDefaultsOptions { High = new SlaTargetOptions { AckMinutes = 60, ResolveMinutes = 30, EscalateAtPercent = 80 } };

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain("Sla:Defaults:High:ResolveMinutes");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Validate_PercentOutOfRange_Fails(int percent)
    {
        var options = new SlaDefaultsOptions { Medium = new SlaTargetOptions { AckMinutes = 10, ResolveMinutes = 20, EscalateAtPercent = percent } };

        var result = _validator.Validate(null, options);

        result.Failures.Should().ContainSingle().Which.Should().Contain("Sla:Defaults:Medium:EscalateAtPercent");
    }

    [Fact]
    public void Validate_NonPositiveMinutes_Fails()
    {
        var options = new SlaDefaultsOptions { Critical = new SlaTargetOptions { AckMinutes = 0, ResolveMinutes = 0, EscalateAtPercent = 80 } };

        var result = _validator.Validate(null, options);

        result.Failures.Should().Contain(f => f.Contains("Critical:AckMinutes"))
            .And.Contain(f => f.Contains("Critical:ResolveMinutes"));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1, 1, 99)]
    [InlineData(15, 240, 80)]
    public void Validate_AcceptedValues_ProduceValidPolicy(int ackMinutes, int resolveMinutes, int percent)
    {
        var options = new SlaDefaultsOptions
        {
            Critical = new SlaTargetOptions { AckMinutes = ackMinutes, ResolveMinutes = resolveMinutes, EscalateAtPercent = percent },
        };

        _validator.Validate(null, options).Succeeded.Should().BeTrue();
        var toPolicy = () => options.ToPolicy(Severity.Critical);
        toPolicy.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Validate_RejectedPercent_AlsoRejectedByPolicy(int percent)
    {
        var options = new SlaDefaultsOptions
        {
            Critical = new SlaTargetOptions { AckMinutes = 10, ResolveMinutes = 20, EscalateAtPercent = percent },
        };

        _validator.Validate(null, options).Failed.Should().BeTrue();
        var toPolicy = () => options.ToPolicy(Severity.Critical);
        toPolicy.Should().Throw<ArgumentOutOfRangeException>();
    }
}
