using FluentAssertions;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Users.GetCurrentUser;
using IncidentHub.Domain.Users;
using NSubstitute;

namespace IncidentHub.Application.UnitTests.Users;

public sealed class GetCurrentUserHandlerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IAppTeamReader _teams = Substitute.For<IAppTeamReader>();

    private GetCurrentUserHandler Handler(IReadOnlySet<UserRole> roles, params Guid[] teamIds)
    {
        _user.UserId.Returns(_userId);
        _user.DisplayName.Returns("Ada Lovelace");
        _user.Email.Returns("ada@test.local");
        _user.Roles.Returns(roles);
        _teams.GetTeamIdsForUserAsync(_userId, Arg.Any<CancellationToken>()).Returns(teamIds);
        return new GetCurrentUserHandler(_user, _teams);
    }

    [Fact]
    public async Task Handler_ReturnsIdentityRolesAndTeamIds()
    {
        var team = Guid.NewGuid();
        var handler = Handler(new HashSet<UserRole> { UserRole.Responder }, team);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.Id.Should().Be(_userId);
        result.DisplayName.Should().Be("Ada Lovelace");
        result.Email.Should().Be("ada@test.local");
        result.Roles.Should().Equal(UserRole.Responder);
        result.TeamIds.Should().Equal(team);
    }

    [Fact]
    public async Task Handler_NoTeams_ReturnsEmptyTeamIds()
    {
        var handler = Handler(new HashSet<UserRole> { UserRole.Reporter });

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.TeamIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Handler_RolesAndTeamsAreSorted()
    {
        var low = new Guid("00000000-0000-0000-0000-000000000001");
        var high = new Guid("ffffffff-0000-0000-0000-000000000001");
        var handler = Handler(new HashSet<UserRole> { UserRole.Admin, UserRole.Reporter, UserRole.TeamLead }, high, low);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.Roles.Should().Equal(UserRole.Reporter, UserRole.TeamLead, UserRole.Admin);
        result.TeamIds.Should().Equal(low, high);
    }
}
