using System.Security.Claims;
using FluentAssertions;
using IncidentHub.Api.Auth;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace IncidentHub.Api.UnitTests.Auth;

public sealed class GroupRoleClaimsTransformationTests
{
    private static readonly string ReportersGroup = Guid.NewGuid().ToString();
    private static readonly string RespondersGroup = Guid.NewGuid().ToString();
    private static readonly string AdminsGroup = Guid.NewGuid().ToString();

    private readonly Guid _userId = Guid.NewGuid();
    private readonly IUserDirectory _users = Substitute.For<IUserDirectory>();
    private readonly FakeLogger<GroupRoleClaimsTransformation> _logger = new();
    private readonly GroupRoleClaimsTransformation _sut;

    public GroupRoleClaimsTransformationTests()
    {
        _users.EnsureUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_userId);

        var options = Options.Create(new GroupRoleOptions
        {
            GroupRoleMap = new Dictionary<string, UserRole>
            {
                [ReportersGroup] = UserRole.Reporter,
                [RespondersGroup] = UserRole.Responder,
                [AdminsGroup] = UserRole.Admin,
            },
        });

        _sut = new GroupRoleClaimsTransformation(options, _users, new HttpContextAccessor(), _logger);
    }

    [Fact]
    public async Task TransformAsync_SingleKnownGroup_AddsMatchingRole()
    {
        var principal = Principal(groups: [RespondersGroup]);

        var result = await _sut.TransformAsync(principal);

        result.GetRoles().Should().BeEquivalentTo([UserRole.Responder]);
    }

    [Fact]
    public async Task TransformAsync_MultipleGroups_AddsUnionOfRoles()
    {
        var principal = Principal(groups: [ReportersGroup, AdminsGroup]);

        var result = await _sut.TransformAsync(principal);

        result.GetRoles().Should().BeEquivalentTo([UserRole.Reporter, UserRole.Admin]);
    }

    [Fact]
    public async Task TransformAsync_GroupIdInDifferentCase_StillMapsRole()
    {
        var principal = Principal(groups: [RespondersGroup.ToUpperInvariant()]);

        var result = await _sut.TransformAsync(principal);

        result.GetRoles().Should().BeEquivalentTo([UserRole.Responder]);
    }

    [Fact]
    public async Task TransformAsync_UnknownGroup_IsIgnored()
    {
        var principal = Principal(groups: [Guid.NewGuid().ToString()]);

        var result = await _sut.TransformAsync(principal);

        result.GetRoles().Should().BeEmpty();
    }

    [Fact]
    public async Task TransformAsync_NoGroups_GrantsNoRolesButAddsUserId()
    {
        var principal = Principal(groups: []);

        var result = await _sut.TransformAsync(principal);

        result.GetRoles().Should().BeEmpty();
        result.GetUserId().Should().Be(_userId);
    }

    [Fact]
    public async Task TransformAsync_GroupOverageIndicated_GrantsNoRolesAndLogsWarning()
    {
        var principal = Principal(groups: [], extra: [new Claim(AuthClaims.HasGroups, "true")]);

        var result = await _sut.TransformAsync(principal);

        result.GetRoles().Should().BeEmpty();
        _logger.Collector.GetSnapshot().Should().Contain(r => r.Level == LogLevel.Warning && r.Message.Contains("overage"));
    }

    [Fact]
    public async Task TransformAsync_ClaimNamesContainsGroups_TreatedAsOverage()
    {
        var principal = Principal(groups: [], extra: [new Claim(AuthClaims.ClaimNames, "{\"groups\":\"src1\"}")]);

        await _sut.TransformAsync(principal);

        _logger.Collector.GetSnapshot().Should().Contain(r => r.Level == LogLevel.Warning && r.Message.Contains("overage"));
    }

    [Fact]
    public async Task TransformAsync_CalledTwiceOnSamePrincipal_DoesNotDuplicateRolesOrHitDirectoryAgain()
    {
        var principal = Principal(groups: [RespondersGroup]);

        await _sut.TransformAsync(principal);
        var result = await _sut.TransformAsync(principal);

        result.FindAll(ClaimTypes.Role).Should().HaveCount(1);
        await _users.Received(1).EnsureUserAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransformAsync_TokenWithoutObjectId_GrantsNoRolesAndSkipsDirectory()
    {
        var identity = new ClaimsIdentity([new Claim(AuthClaims.Groups, RespondersGroup)], "test");

        var result = await _sut.TransformAsync(new ClaimsPrincipal(identity));

        result.GetRoles().Should().BeEmpty();
        result.GetUserId().Should().BeNull();
        await _users.DidNotReceiveWithAnyArgs().EnsureUserAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task TransformAsync_UnauthenticatedPrincipal_ReturnedUntouched()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await _sut.TransformAsync(principal);

        result.Identities.Should().HaveCount(1);
    }

    [Fact]
    public async Task TransformAsync_EmailFromPreferredUsername_PassedToDirectory()
    {
        var principal = Principal(groups: [], extra: [new Claim("preferred_username", "jane@contoso.com")]);

        await _sut.TransformAsync(principal);

        await _users.Received(1).EnsureUserAsync("oid-1", "Jane", "jane@contoso.com", Arg.Any<CancellationToken>());
    }

    private static ClaimsPrincipal Principal(IEnumerable<string> groups, IEnumerable<Claim>? extra = null)
    {
        var claims = new List<Claim> { new(AuthClaims.ObjectId, "oid-1"), new(AuthClaims.Name, "Jane") };
        claims.AddRange(groups.Select(g => new Claim(AuthClaims.Groups, g)));
        claims.AddRange(extra ?? []);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
