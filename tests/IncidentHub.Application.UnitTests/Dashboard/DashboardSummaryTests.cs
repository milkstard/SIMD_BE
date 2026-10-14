using FluentAssertions;
using FluentValidation.TestHelper;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Dashboard;
using IncidentHub.Application.Dashboard.Dtos;
using IncidentHub.Application.Dashboard.GetDashboardSummary;
using IncidentHub.Application.Realtime;
using IncidentHub.Domain.Incidents;
using NSubstitute;

namespace IncidentHub.Application.UnitTests.Dashboard;

public sealed class DashboardSummaryTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static DashboardSummaryDto Empty(string range) =>
        new(range, DateTimeOffset.UtcNow, 0, 0, 0, new Dictionary<Severity, int>(), [], null, null, []);

    [Theory]
    [InlineData("1h")]
    [InlineData("")]
    [InlineData("7D")]
    [InlineData("90d")]
    public void Validator_InvalidRange_Fails(string range)
    {
        new GetDashboardSummaryValidator().TestValidate(new GetDashboardSummaryQuery(range)).ShouldHaveValidationErrorFor(x => x.Range);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("24h")]
    [InlineData("7d")]
    [InlineData("30d")]
    public void Validator_ValidOrMissingRange_Passes(string? range)
    {
        new GetDashboardSummaryValidator().TestValidate(new GetDashboardSummaryQuery(range)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void TryParse_MissingRange_UsesDefault()
    {
        DashboardRanges.TryParse(null, out var range).Should().BeTrue();

        range.Should().Be(DashboardRange.D7);
    }

    [Fact]
    public async Task Handler_ResolvesCallerTeams_PassesThemToProvider()
    {
        var userId = Guid.NewGuid();
        var teams = Substitute.For<IAppTeamReader>();
        var teamIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        teams.GetTeamIdsForUserAsync(userId, Arg.Any<CancellationToken>()).Returns(teamIds);
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        var provider = Substitute.For<IDashboardSummaryProvider>();
        var expected = Empty("30d");
        provider.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), DashboardRange.D30, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await new GetDashboardSummaryHandler(teams, provider, user).Handle(new GetDashboardSummaryQuery("30d"), CancellationToken.None);

        result.Should().BeSameAs(expected);
        await provider.Received(1).GetAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(teamIds)), DashboardRange.D30, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(DashboardRange.H24, "2026-10-14T00:00:00Z")]
    [InlineData(DashboardRange.D7, "2026-10-08T00:00:00Z")]
    [InlineData(DashboardRange.D30, "2026-09-15T00:00:00Z")]
    public void WindowStart_UsesTimeProvider(DashboardRange range, string expected)
    {
        var time = new FixedTime(DateTimeOffset.Parse("2026-10-14T15:42:10Z"));

        range.WindowStart(time).Should().Be(DateTimeOffset.Parse(expected));
    }

    [Fact]
    public async Task DashboardSummaryReader_ProvidesSummaryForTeam()
    {
        var teamId = Guid.NewGuid();
        var provider = Substitute.For<IDashboardSummaryProvider>();
        var expected = Empty("7d");
        provider.RefreshAsync(Arg.Any<IReadOnlyCollection<Guid>>(), DashboardRange.D7, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await new DashboardSummaryReader(provider).GetAsync(teamId, CancellationToken.None);

        result.Should().BeSameAs(expected);
        await provider.Received(1).RefreshAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == teamId), DashboardRange.D7, Arg.Any<CancellationToken>());
    }
}
