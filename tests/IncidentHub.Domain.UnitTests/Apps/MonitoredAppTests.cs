using FluentAssertions;
using IncidentHub.Domain.Apps;

namespace IncidentHub.Domain.UnitTests.Apps;

public sealed class MonitoredAppTests
{
    private static readonly Guid TeamId = Guid.NewGuid();

    [Fact]
    public void Create_ValidValues_SetsFieldsAndDefaultsActive()
    {
        var userId = Guid.NewGuid();

        var app = MonitoredApp.Create("Checkout", "CHK", TeamId, userId, [AppEnvironment.Production, AppEnvironment.UAT]);

        app.Name.Should().Be("Checkout");
        app.OwningTeamId.Should().Be(TeamId);
        app.EscalationUserId.Should().Be(userId);
        app.Environments.Should().Equal(AppEnvironment.Production, AppEnvironment.UAT);
        app.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_Throws(string name)
    {
        var act = () => MonitoredApp.Create(name, "CHK", TeamId, null, [AppEnvironment.Production]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_CodeLowercase_StoresUpperCase()
    {
        var app = MonitoredApp.Create("Checkout", "chk-web", TeamId, null, [AppEnvironment.Production]);

        app.Code.Should().Be("CHK-WEB");
    }

    [Fact]
    public void Create_CodeTooLong_Throws()
    {
        var act = () => MonitoredApp.Create("Checkout", new string('a', 21), TeamId, null, [AppEnvironment.Production]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_EmptyOwningTeamId_Throws()
    {
        var act = () => MonitoredApp.Create("Checkout", "CHK", Guid.Empty, null, [AppEnvironment.Production]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_EmptyEnvironments_Throws()
    {
        var act = () => MonitoredApp.Create("Checkout", "CHK", TeamId, null, []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_DuplicateEnvironments_Throws()
    {
        var act = () => MonitoredApp.Create("Checkout", "CHK", TeamId, null, [AppEnvironment.UAT, AppEnvironment.UAT]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_ValidValues_ChangesFields()
    {
        var app = MonitoredApp.Create("Checkout", "CHK", TeamId, null, [AppEnvironment.Production]);
        var newTeam = Guid.NewGuid();

        app.Update("Payments", "pay", newTeam, null, [AppEnvironment.Development], isActive: false);

        app.Name.Should().Be("Payments");
        app.Code.Should().Be("PAY");
        app.OwningTeamId.Should().Be(newTeam);
        app.Environments.Should().Equal(AppEnvironment.Development);
        app.IsActive.Should().BeFalse();
    }
}
