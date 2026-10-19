using FluentAssertions;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;

namespace IncidentHub.Domain.UnitTests.Incidents.Sla;

public sealed class SlaPolicySettingTests
{
    [Fact]
    public void Create_ValidValues_SetsProperties()
    {
        var setting = SlaPolicySetting.Create(Severity.High, 60, 480, 80);

        setting.Severity.Should().Be(Severity.High);
        setting.AckMinutes.Should().Be(60);
        setting.ResolveMinutes.Should().Be(480);
        setting.EscalateAtPercent.Should().Be(80);
    }

    [Fact]
    public void Update_ValidValues_ReplacesTargetsAndKeepsSeverity()
    {
        var setting = SlaPolicySetting.Create(Severity.High, 60, 480, 80);

        setting.Update(30, 120, 50);

        setting.Severity.Should().Be(Severity.High);
        setting.AckMinutes.Should().Be(30);
        setting.ResolveMinutes.Should().Be(120);
        setting.EscalateAtPercent.Should().Be(50);
    }

    [Theory]
    [InlineData(0, 10, 80)]
    [InlineData(60, 59, 80)]
    [InlineData(5, 10, 0)]
    [InlineData(5, 10, 100)]
    [InlineData(5, SlaPolicySetting.MaxResolveMinutes + 1, 80)]
    public void Update_InvalidValues_ThrowsAndChangesNothing(int ack, int resolve, int percent)
    {
        var setting = SlaPolicySetting.Create(Severity.Low, 480, 7_200, 80);

        var act = () => setting.Update(ack, resolve, percent);

        act.Should().Throw<ArgumentOutOfRangeException>();
        setting.AckMinutes.Should().Be(480);
        setting.ResolveMinutes.Should().Be(7_200);
        setting.EscalateAtPercent.Should().Be(80);
    }

    [Fact]
    public void Update_ResolveAtMaximum_Succeeds()
    {
        var setting = SlaPolicySetting.Create(Severity.Low, 480, 7_200, 80);

        setting.Update(480, SlaPolicySetting.MaxResolveMinutes, 80);

        setting.ResolveMinutes.Should().Be(SlaPolicySetting.MaxResolveMinutes);
    }

    [Fact]
    public void ToPolicy_ReturnsTheStoredTargets()
    {
        var policy = SlaPolicySetting.Create(Severity.Critical, 15, 240, 80).ToPolicy();

        policy.Should().Be(new SlaPolicy(Severity.Critical, 15, 240, 80));
    }
}
