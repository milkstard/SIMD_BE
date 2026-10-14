using FluentAssertions;
using IncidentHub.Domain.Apps;
using IncidentHub.Domain.Incidents;

namespace IncidentHub.Domain.UnitTests.Apps;

public sealed class AppStatusCalculatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private static OpenIncidentFact Fact(Severity severity, AppEnvironment env = AppEnvironment.Production, int minutes = 0) =>
        new(severity, env, T0.AddMinutes(minutes));

    [Fact]
    public void Calculate_CriticalInProduction_ReturnsOutage()
    {
        var result = AppStatusCalculator.Calculate([Fact(Severity.Critical)]);

        result.Status.Should().Be(AppStatus.Outage);
    }

    [Fact]
    public void Calculate_CriticalInUat_ReturnsDegraded()
    {
        AppStatusCalculator.Calculate([Fact(Severity.Critical, AppEnvironment.UAT)]).Status.Should().Be(AppStatus.Degraded);
    }

    [Fact]
    public void Calculate_HighOnly_ReturnsDegraded()
    {
        AppStatusCalculator.Calculate([Fact(Severity.High, AppEnvironment.Development)]).Status.Should().Be(AppStatus.Degraded);
    }

    [Fact]
    public void Calculate_MediumAndLow_ReturnsOperational()
    {
        var result = AppStatusCalculator.Calculate([Fact(Severity.Medium), Fact(Severity.Low)]);

        result.Status.Should().Be(AppStatus.Operational);
        result.OpenIncidents.Should().Be(2);
        result.Since.Should().BeNull();
    }

    [Fact]
    public void Calculate_NoOpenIncidents_ReturnsOperationalWithNoSince()
    {
        var result = AppStatusCalculator.Calculate([]);

        result.Should().Be(new AppStatusResult(AppStatus.Operational, 0, null, null));
    }

    [Fact]
    public void Calculate_TwoCauses_SinceIsEarliest()
    {
        var result = AppStatusCalculator.Calculate(
            [Fact(Severity.Critical, minutes: 30), Fact(Severity.Critical, minutes: 10), Fact(Severity.High, minutes: -60)]);

        result.Status.Should().Be(AppStatus.Outage);
        result.Since.Should().Be(T0.AddMinutes(10));
    }

    [Fact]
    public void Calculate_OutageWithEarlierDegradedCause_SinceIgnoresNonCausingIncidents()
    {
        var result = AppStatusCalculator.Calculate([Fact(Severity.High, minutes: -60), Fact(Severity.Critical, minutes: 5)]);

        result.Since.Should().Be(T0.AddMinutes(5));
    }

    [Fact]
    public void Calculate_WorstSeverity_IsMostSevereOpen()
    {
        var result = AppStatusCalculator.Calculate([Fact(Severity.Low), Fact(Severity.High), Fact(Severity.Medium)]);

        result.WorstOpenSeverity.Should().Be(Severity.High);
        result.OpenIncidents.Should().Be(3);
    }
}
