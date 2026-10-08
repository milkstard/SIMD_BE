using FluentAssertions;
using IncidentHub.Api.Auth;
using IncidentHub.Domain.Users;

namespace IncidentHub.Api.UnitTests.Auth;

public sealed class AuthOptionsValidationTests
{
    private static readonly AzureAdSettingsValidator AzureAd = new();
    private static readonly GroupRoleOptionsValidator Groups = new();

    [Fact]
    public void Validate_CompleteAzureAdSettings_Succeeds()
    {
        var result = AzureAd.Validate(null, new AzureAdSettings { TenantId = Guid.NewGuid().ToString(), ClientId = Guid.NewGuid().ToString() });

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "client")]
    [InlineData("tenant", "")]
    [InlineData("", "")]
    public void Validate_MissingTenantOrClient_Fails(string tenantId, string clientId)
    {
        var result = AzureAd.Validate(null, new AzureAdSettings { TenantId = tenantId, ClientId = clientId });

        result.Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("Consumers")]
    public void Validate_MultiTenantAlias_Fails(string tenantId)
    {
        var result = AzureAd.Validate(null, new AzureAdSettings { TenantId = tenantId, ClientId = "client" });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("multi-tenant");
    }

    [Fact]
    public void Validate_EmptyGroupRoleMap_Fails()
    {
        var result = Groups.Validate(null, new GroupRoleOptions());

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_NonGuidGroupKey_Fails()
    {
        var options = new GroupRoleOptions { GroupRoleMap = { ["not-a-guid"] = UserRole.Admin } };

        var result = Groups.Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_GuidGroupKeys_Succeeds()
    {
        var options = new GroupRoleOptions { GroupRoleMap = { [Guid.NewGuid().ToString()] = UserRole.Reporter } };

        var result = Groups.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }
}
