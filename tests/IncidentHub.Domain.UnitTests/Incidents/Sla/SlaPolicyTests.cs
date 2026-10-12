using FluentAssertions;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;

namespace IncidentHub.Domain.UnitTests.Incidents.Sla;

public sealed class SlaPolicyTests
{
    [Fact]
    public void Ctor_ValidValues_SetsProperties()
    {
        var policy = new SlaPolicy(Severity.Critical, 15, 240, 80);

        policy.Severity.Should().Be(Severity.Critical);
        policy.AckMinutes.Should().Be(15);
        policy.ResolveMinutes.Should().Be(240);
        policy.EscalateAtPercent.Should().Be(80);
    }

    [Fact]
    public void Ctor_ResolveEqualToAck_Succeeds()
    {
        var act = () => new SlaPolicy(Severity.Low, 60, 60, 80);

        act.Should().NotThrow();
    }

    [Fact]
    public void Ctor_ResolveLessThanAck_Throws()
    {
        var act = () => new SlaPolicy(Severity.Low, 60, 59, 80);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    public void Ctor_NonPositiveMinutes_Throws(int ackMinutes, int resolveMinutes)
    {
        var act = () => new SlaPolicy(Severity.High, ackMinutes, resolveMinutes, 80);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(-1)]
    public void Ctor_PercentOutOfRange_Throws(int percent)
    {
        var act = () => new SlaPolicy(Severity.High, 10, 20, percent);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
