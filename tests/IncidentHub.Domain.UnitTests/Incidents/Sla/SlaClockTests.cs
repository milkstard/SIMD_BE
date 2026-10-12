using FluentAssertions;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Sla;

namespace IncidentHub.Domain.UnitTests.Incidents.Sla;

public sealed class SlaClockTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(Severity.Critical, 15, 240)]
    [InlineData(Severity.High, 60, 480)]
    [InlineData(Severity.Medium, 240, 4_320)]
    [InlineData(Severity.Low, 480, 7_200)]
    public void DueTimes_EachSeverity_AddsPolicyMinutes(Severity severity, int ackMinutes, int resolveMinutes)
    {
        var policy = new SlaPolicy(severity, ackMinutes, resolveMinutes, 80);

        var due = SlaClock.DueTimes(policy, T0);

        due.AckDueAt.Should().Be(T0.AddMinutes(ackMinutes));
        due.ResolveDueAt.Should().Be(T0.AddMinutes(resolveMinutes));
    }

    [Fact]
    public void DueTimes_NonUtcOffset_ReturnsUtc()
    {
        var policy = new SlaPolicy(Severity.High, 60, 480, 80);
        var local = new DateTimeOffset(2026, 10, 12, 11, 0, 0, TimeSpan.FromHours(2));

        var due = SlaClock.DueTimes(policy, local);

        due.AckDueAt.Offset.Should().Be(TimeSpan.Zero);
        due.ResolveDueAt.Offset.Should().Be(TimeSpan.Zero);
        due.AckDueAt.Should().Be(T0.AddMinutes(60));
    }

    [Fact]
    public void DueTimes_NullPolicy_Throws()
    {
        var act = () => SlaClock.DueTimes(null!, T0);

        act.Should().Throw<ArgumentNullException>();
    }
}
