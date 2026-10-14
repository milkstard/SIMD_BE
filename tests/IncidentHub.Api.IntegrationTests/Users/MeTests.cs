using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Users;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MeTests(ApiFactory factory)
{
    private const string Url = "/api/v1/me";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>());

    private async Task<Guid> TeamOfAsync(Guid appId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Applications.Where(a => a.Id == appId).Select(a => a.OwningTeamId).SingleAsync();
    }

    [Fact]
    public async Task Get_Responder_ReturnsRolesAndTeams()
    {
        var appId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var userId = await factory.EnsureUserAsync(oid);
        var teamId = await TeamOfAsync(appId);

        var response = await factory.CreateClient().AsUser(factory, oid, UserRole.Responder).GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("id").GetGuid().Should().Be(userId);
        body.GetProperty("displayName").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("email").GetString().Should().Be($"{oid}@test.local");
        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Responder");
        body.GetProperty("teamIds").EnumerateArray().Select(t => t.GetGuid()).Should().Equal(teamId);
    }

    [Fact]
    public async Task Get_Admin_ReturnsAdminRole()
    {
        var oid = Guid.NewGuid().ToString();

        var response = await factory.CreateClient().AsUser(factory, oid, UserRole.Admin).GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Contain("Admin");
    }

    [Fact]
    public async Task Get_NoTeam_ReturnsEmptyTeamIds()
    {
        var oid = Guid.NewGuid().ToString();

        var response = await factory.CreateClient().AsUser(factory, oid, UserRole.Reporter).GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("teamIds").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
