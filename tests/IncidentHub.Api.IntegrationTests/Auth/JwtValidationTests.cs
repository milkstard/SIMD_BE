using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Users;

namespace IncidentHub.Api.IntegrationTests.Auth;

/// <summary>Real JWT bearer validation (signature, lifetime, audience, tenant) against a locally signed key.</summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class JwtValidationTests(ApiFactory factory)
{
    private string Audience => $"api://{factory.ClientId}";

    private string Token(
        UserRole role = UserRole.Responder,
        string? audience = null,
        DateTime? expires = null,
        string? tokenTenantId = null,
        string scope = "access_as_user") =>
        factory.Jwt.Create(
            factory.TenantId,
            audience ?? Audience,
            Guid.NewGuid().ToString(),
            [factory.GroupFor(role)],
            scope,
            expires,
            tokenTenantId);

    private HttpClient ClientWithBearer(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Theory]
    [InlineData(UserRole.Reporter)]
    [InlineData(UserRole.Responder)]
    [InlineData(UserRole.TeamLead)]
    [InlineData(UserRole.Admin)]
    public async Task GetMe_TokenInRoleGroup_CurrentUserRolesMatchTheGroup(UserRole role)
    {
        var objectId = Guid.NewGuid().ToString();
        var token = factory.Jwt.Create(factory.TenantId, Audience, objectId, [factory.GroupFor(role)], "access_as_user");

        var response = await ClientWithBearer(token).GetAsync("/probe/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<AuthProbeController.MeResponse>();
        me!.Roles.Should().Equal(role.ToString());
        me.EntraObjectId.Should().Be(objectId);
    }

    [Fact]
    public async Task GetMe_TokenInSeveralGroups_CurrentUserRolesAreTheUnion()
    {
        var token = factory.Jwt.Create(
            factory.TenantId,
            Audience,
            Guid.NewGuid().ToString(),
            [factory.GroupFor(UserRole.Reporter), factory.GroupFor(UserRole.TeamLead)],
            "access_as_user");

        var response = await ClientWithBearer(token).GetAsync("/probe/me");

        var me = await response.Content.ReadFromJsonAsync<AuthProbeController.MeResponse>();
        me!.Roles.Should().Equal(nameof(UserRole.Reporter), nameof(UserRole.TeamLead));
    }

    [Fact]
    public async Task GetPolicyEndpoint_ValidToken_MapsGroupToRoleAndReturns200()
    {
        var response = await ClientWithBearer(Token(UserRole.Responder)).GetAsync("/probe/policy/assign");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPolicyEndpoint_ValidTokenWithLowerRole_Returns403()
    {
        var response = await ClientWithBearer(Token(UserRole.Reporter)).GetAsync("/probe/policy/assign");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPolicyEndpoint_ClientIdAsAudience_Returns200()
    {
        var response = await ClientWithBearer(Token(audience: factory.ClientId)).GetAsync("/probe/policy/view");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPolicyEndpoint_ExpiredToken_Returns401()
    {
        var token = Token(expires: DateTime.UtcNow.AddMinutes(-30));

        var response = await ClientWithBearer(token).GetAsync("/probe/policy/view");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPolicyEndpoint_WrongAudience_Returns401()
    {
        var response = await ClientWithBearer(Token(audience: "api://someone-else")).GetAsync("/probe/policy/view");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPolicyEndpoint_TokenFromDifferentTenant_Returns401()
    {
        var token = Token(tokenTenantId: Guid.NewGuid().ToString());

        var response = await ClientWithBearer(token).GetAsync("/probe/policy/view");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPolicyEndpoint_TokenWithoutRequiredScope_Returns403()
    {
        var response = await ClientWithBearer(Token(scope: "something_else")).GetAsync("/probe/policy/view");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPolicyEndpoint_GarbageToken_Returns401()
    {
        var response = await ClientWithBearer("not-a-jwt").GetAsync("/probe/policy/view");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPolicyEndpoint_TokenInQueryStringOutsideHubs_Returns401()
    {
        var response = await factory.CreateClient().GetAsync($"/probe/policy/view?access_token={Token()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NegotiateHub_TokenInQueryString_Returns200()
    {
        var response = await factory.CreateClient()
            .PostAsync($"/hubs/incidents/negotiate?negotiateVersion=1&access_token={Token()}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NegotiateHub_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().PostAsync("/hubs/incidents/negotiate?negotiateVersion=1", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NegotiateHub_UserWithNoMappedRole_Returns403()
    {
        var token = factory.Jwt.Create(factory.TenantId, Audience, Guid.NewGuid().ToString(), groups: []);

        var response = await factory.CreateClient()
            .PostAsync($"/hubs/incidents/negotiate?negotiateVersion=1&access_token={token}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
