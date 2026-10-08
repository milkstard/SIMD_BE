using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Users;

namespace IncidentHub.Api.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PolicyAuthorizationTests(ApiFactory factory)
{
    private static string NewOid() => Guid.NewGuid().ToString();

    [Fact]
    public async Task GetProtectedEndpoint_WithoutToken_Returns401ProblemDetailsWithTraceId()
    {
        var response = await factory.CreateClient().GetAsync("/probe/fallback");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().ContainSingle(h => h.Scheme == "Bearer");
        await AssertProblemDetails(response, 401);
    }

    [Fact]
    public async Task GetAnonymousEndpoint_WithoutToken_Returns200()
    {
        var response = await factory.CreateClient().GetAsync("/probe/anonymous");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetFallbackEndpoint_AuthenticatedUserWithoutRoles_Returns200()
    {
        var client = factory.CreateClient().AsUser(factory, NewOid());

        var response = await client.GetAsync("/probe/fallback");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetFallbackEndpoint_TokenWithoutRequiredScope_Returns403ProblemDetails()
    {
        var client = factory.CreateClient().AsUser(factory, NewOid(), [UserRole.Admin], scope: "other_scope");

        var response = await client.GetAsync("/probe/fallback");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertProblemDetails(response, 403);
    }

    [Theory]
    [InlineData("policy/report", UserRole.Reporter, HttpStatusCode.OK)]
    [InlineData("policy/report", UserRole.Admin, HttpStatusCode.OK)]
    [InlineData("policy/view", UserRole.Reporter, HttpStatusCode.OK)]
    [InlineData("policy/transition", UserRole.Reporter, HttpStatusCode.OK)]
    [InlineData("policy/assign", UserRole.Reporter, HttpStatusCode.Forbidden)]
    [InlineData("policy/assign", UserRole.Responder, HttpStatusCode.OK)]
    [InlineData("policy/assign", UserRole.TeamLead, HttpStatusCode.OK)]
    [InlineData("policy/comment-internal", UserRole.Reporter, HttpStatusCode.Forbidden)]
    [InlineData("policy/comment-internal", UserRole.Responder, HttpStatusCode.OK)]
    [InlineData("policy/manage", UserRole.Reporter, HttpStatusCode.Forbidden)]
    [InlineData("policy/manage", UserRole.Responder, HttpStatusCode.Forbidden)]
    [InlineData("policy/manage", UserRole.TeamLead, HttpStatusCode.OK)]
    [InlineData("policy/manage", UserRole.Admin, HttpStatusCode.OK)]
    [InlineData("policy/dashboard", UserRole.Reporter, HttpStatusCode.OK)]
    public async Task GetPolicyEndpoint_ByRole_ReturnsExpectedStatus(string path, UserRole role, HttpStatusCode expected)
    {
        var client = factory.CreateClient().AsUser(factory, NewOid(), role);

        var response = await client.GetAsync($"/probe/{path}");

        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("policy/report")]
    [InlineData("policy/view")]
    [InlineData("policy/transition")]
    [InlineData("policy/assign")]
    [InlineData("policy/comment-internal")]
    [InlineData("policy/manage")]
    [InlineData("policy/dashboard")]
    public async Task GetPolicyEndpoint_UserWithNoMappedRole_Returns403ProblemDetails(string path)
    {
        var client = factory.CreateClient().AsUser(factory, NewOid());

        var response = await client.GetAsync($"/probe/{path}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertProblemDetails(response, 403);
    }

    [Fact]
    public async Task GetPolicyEndpoint_UserInUnmappedGroup_Returns403()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.OidHeader, NewOid());
        client.DefaultRequestHeaders.Add(TestAuthHandler.GroupsHeader, Guid.NewGuid().ToString());

        var response = await client.GetAsync("/probe/policy/view");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReadIncident_TeamMember_Returns200()
    {
        var oid = NewOid();
        var appId = await factory.CreateAppAsync();
        await factory.AddToTeamAsync(oid, appId);
        var client = factory.CreateClient().AsUser(factory, oid, UserRole.Reporter);

        var response = await client.GetAsync($"/probe/apps/{appId}/incident");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadIncident_NonMember_Returns404ToHideExistence()
    {
        var appId = await factory.CreateAppAsync();
        var client = factory.CreateClient().AsUser(factory, NewOid(), UserRole.Responder);

        var response = await client.GetAsync($"/probe/apps/{appId}/incident");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WriteIncident_TeamMember_Returns200()
    {
        var oid = NewOid();
        var appId = await factory.CreateAppAsync();
        await factory.AddToTeamAsync(oid, appId);
        var client = factory.CreateClient().AsUser(factory, oid, UserRole.Responder);

        var response = await client.PostAsync($"/probe/apps/{appId}/transition", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WriteIncident_NonMember_Returns403()
    {
        var appId = await factory.CreateAppAsync();
        var client = factory.CreateClient().AsUser(factory, NewOid(), UserRole.Responder);

        var response = await client.PostAsync($"/probe/apps/{appId}/transition", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WriteIncident_AdminWhoIsNotOnTeam_Returns403BecauseThereIsNoGlobalBypass()
    {
        var appId = await factory.CreateAppAsync();
        var client = factory.CreateClient().AsUser(factory, NewOid(), UserRole.Admin);

        var response = await client.PostAsync($"/probe/apps/{appId}/transition", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ManageApp_AdminWhoIsNotOnTeam_Returns200()
    {
        var appId = await factory.CreateAppAsync();
        var client = factory.CreateClient().AsUser(factory, NewOid(), UserRole.Admin);

        var response = await client.PostAsync($"/probe/apps/{appId}/manage", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ManageApp_TeamLeadOnOwnApp_Returns200()
    {
        var oid = NewOid();
        var appId = await factory.CreateAppAsync();
        await factory.AddToTeamAsync(oid, appId);
        var client = factory.CreateClient().AsUser(factory, oid, UserRole.TeamLead);

        var response = await client.PostAsync($"/probe/apps/{appId}/manage", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ManageApp_TeamLeadOnOtherApp_Returns403()
    {
        var appId = await factory.CreateAppAsync();
        var client = factory.CreateClient().AsUser(factory, NewOid(), UserRole.TeamLead);

        var response = await client.PostAsync($"/probe/apps/{appId}/manage", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task AssertProblemDetails(HttpResponseMessage response, int status)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().Should().Be(status);
        body.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
