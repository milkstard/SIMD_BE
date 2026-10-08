using System.Security.Claims;
using FluentAssertions;
using IncidentHub.Api.Auth;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace IncidentHub.Api.UnitTests.Auth;

public sealed class AppTeamAuthorizationHandlerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _appId = Guid.NewGuid();
    private readonly IAppTeamReader _reader = Substitute.For<IAppTeamReader>();
    private readonly AppTeamAuthorizationHandler _sut;

    public AppTeamAuthorizationHandlerTests() =>
        _sut = new AppTeamAuthorizationHandler(_reader, NullLogger<AppTeamAuthorizationHandler>.Instance);

    [Fact]
    public async Task HandleAsync_UserIsTeamMember_Succeeds()
    {
        _reader.IsMemberAsync(_userId, _appId, Arg.Any<CancellationToken>()).Returns(true);

        var context = await Evaluate(AppTeamRequirement.Member, UserRole.Responder);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_UserIsNotTeamMember_Fails()
    {
        _reader.IsMemberAsync(_userId, _appId, Arg.Any<CancellationToken>()).Returns(false);

        var context = await Evaluate(AppTeamRequirement.Member, UserRole.Responder);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_AdminNotOnTeamWithoutBypass_Fails()
    {
        _reader.IsMemberAsync(_userId, _appId, Arg.Any<CancellationToken>()).Returns(false);

        var context = await Evaluate(AppTeamRequirement.Member, UserRole.Admin);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_AdminNotOnTeamWithBypass_SucceedsWithoutHittingReader()
    {
        var context = await Evaluate(AppTeamRequirement.MemberOrAdmin, UserRole.Admin);

        context.HasSucceeded.Should().BeTrue();
        await _reader.DidNotReceiveWithAnyArgs().IsMemberAsync(default, default, default);
    }

    [Fact]
    public async Task HandleAsync_NonAdminNotOnTeamWithBypassRequirement_Fails()
    {
        _reader.IsMemberAsync(_userId, _appId, Arg.Any<CancellationToken>()).Returns(false);

        var context = await Evaluate(AppTeamRequirement.MemberOrAdmin, UserRole.TeamLead);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_PrincipalWithoutUserId_FailsWithoutHittingReader()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, nameof(UserRole.Responder))], "test");

        var context = await Evaluate(AppTeamRequirement.Member, new ClaimsPrincipal(identity));

        context.HasSucceeded.Should().BeFalse();
        await _reader.DidNotReceiveWithAnyArgs().IsMemberAsync(default, default, default);
    }

    private Task<AuthorizationHandlerContext> Evaluate(AppTeamRequirement requirement, UserRole role)
    {
        var identity = new ClaimsIdentity(
            [new Claim(AuthClaims.UserId, _userId.ToString()), new Claim(ClaimTypes.Role, role.ToString())],
            "test");
        return Evaluate(requirement, new ClaimsPrincipal(identity));
    }

    private async Task<AuthorizationHandlerContext> Evaluate(AppTeamRequirement requirement, ClaimsPrincipal user)
    {
        var context = new AuthorizationHandlerContext([requirement], user, new AppScopedResource(_appId));
        await _sut.HandleAsync(context);
        return context;
    }
}
