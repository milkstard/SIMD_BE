using FluentAssertions;
using IncidentHub.Domain.Teams;

namespace IncidentHub.Domain.UnitTests.Teams;

public sealed class TeamTests
{
    [Fact]
    public void Create_ValidValues_SetsFields()
    {
        var team = Team.Create("Payments", "payments@corp.test", "https://hooks.corp.test/abc");

        team.Name.Should().Be("Payments");
        team.Email.Should().Be("payments@corp.test");
        team.TeamsChannelUrl.Should().Be("https://hooks.corp.test/abc");
    }

    [Fact]
    public void Create_NoChannelUrl_Allowed()
    {
        var team = Team.Create("Payments", "payments@corp.test", null);

        team.TeamsChannelUrl.Should().BeNull();
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public void Create_InvalidTeamsChannelUrl_Throws(string url)
    {
        var act = () => Team.Create("Payments", "payments@corp.test", url);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_HttpUrl_Throws()
    {
        var act = () => Team.Create("Payments", "payments@corp.test", "http://hooks.corp.test/abc");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_BlankName_Throws()
    {
        var act = () => Team.Create(" ", "payments@corp.test", null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_ValidValues_ChangesFields()
    {
        var team = Team.Create("Payments", "payments@corp.test", null);

        team.Update("Billing", "billing@corp.test", "https://hooks.corp.test/x");

        team.Name.Should().Be("Billing");
        team.Email.Should().Be("billing@corp.test");
        team.TeamsChannelUrl.Should().Be("https://hooks.corp.test/x");
    }
}
