using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Users;

namespace IncidentHub.Api.IntegrationTests.Apps;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ApplicationsEndpointsTests(ApiFactory factory)
{
    private const string Route = "/api/v1/applications";

    private sealed record TeamRef(Guid Id, string Name);

    private sealed record UserRef(Guid Id, string DisplayName, string Email);

    private sealed record AppResponse(
        Guid Id,
        string Name,
        string Code,
        TeamRef OwningTeam,
        UserRef? EscalationContact,
        List<string> Environments,
        bool IsActive,
        string RowVersion);

    private sealed record PageResponse(List<AppResponse> Items, string? NextCursor);

    private sealed record IdResponse(Guid Id);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static string NewCode() => $"A{Guid.NewGuid():N}"[..14].ToUpperInvariant();

    private static string NewOid() => Guid.NewGuid().ToString();

    private HttpClient Admin() => factory.CreateClient().AsUser(factory, NewOid(), UserRole.Admin);

    private HttpClient As(string oid, UserRole role) => factory.CreateClient().AsUser(factory, oid, role);

    private static async Task<Guid> CreateTeamAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/teams", new { name = Unique("team"), email = "team@acme.com" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private static object Body(
        Guid teamId,
        string? code = null,
        string? name = null,
        Guid? contact = null,
        string[]? environments = null,
        bool? isActive = null) =>
        new
        {
            name = name ?? Unique("app"),
            code = code ?? NewCode(),
            owningTeamId = teamId,
            escalationContactId = contact,
            environments = environments ?? ["Production", "UAT"],
            isActive,
        };

    private static async Task<AppResponse> CreateAppAsync(HttpClient admin, object body)
    {
        var response = await admin.PostAsJsonAsync(Route, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AppResponse>())!;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, object body, string? rowVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"{Route}/{id}") { Content = JsonContent.Create(body) };
        if (rowVersion is not null)
        {
            request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{rowVersion}\""));
        }

        return client.SendAsync(request);
    }

    private static async Task<List<AppResponse>> ListAllAsync(HttpClient client, bool includeInactive = false, int limit = 100)
    {
        var all = new List<AppResponse>();
        string? cursor = null;
        do
        {
            var url = $"{Route}?limit={limit}&includeInactive={includeInactive.ToString().ToLowerInvariant()}"
                + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = (await client.GetFromJsonAsync<PageResponse>(url))!;
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return all;
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, int status)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().Should().Be(status);
        body.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        return body;
    }

    private static async Task<List<string>> ErrorKeysAsync(HttpResponseMessage response) =>
        (await AssertProblemAsync(response, 422)).GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();

    [Theory]
    [InlineData(UserRole.Reporter)]
    [InlineData(UserRole.Responder)]
    [InlineData(UserRole.TeamLead)]
    public async Task PostApplications_NonAdminRole_Returns403(UserRole role)
    {
        var teamId = await CreateTeamAsync(Admin());

        var response = await As(NewOid(), role).PostAsJsonAsync(Route, Body(teamId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertProblemAsync(response, 403);
    }

    [Fact]
    public async Task PostApplications_NoToken_Returns401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(Route, Body(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostApplications_Admin_Returns201WithLocationAndETag()
    {
        var admin = Admin();
        var teamId = await CreateTeamAsync(admin);
        var code = NewCode();

        var response = await admin.PostAsJsonAsync(Route, Body(teamId, code.ToLowerInvariant()));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var app = (await response.Content.ReadFromJsonAsync<AppResponse>())!;
        response.Headers.Location!.ToString().Should().EndWith($"{Route}/{app.Id}");
        response.Headers.ETag!.Tag.Should().Be($"\"{app.RowVersion}\"");
        app.Code.Should().Be(code);
        app.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task PostThenGetApplication_ReturnsIdenticalData()
    {
        var admin = Admin();
        var teamId = await CreateTeamAsync(admin);
        var contactId = await factory.EnsureUserAsync(NewOid());

        var created = await CreateAppAsync(admin, Body(teamId, contact: contactId, environments: ["Development", "Production"]));
        var fetched = await admin.GetFromJsonAsync<AppResponse>($"{Route}/{created.Id}");

        fetched.Should().BeEquivalentTo(created);
        created.OwningTeam.Id.Should().Be(teamId);
        created.EscalationContact!.Id.Should().Be(contactId);
        created.Environments.Should().Equal("Development", "Production");
    }

    [Fact]
    public async Task GetApplications_Admin_IncludesCreated()
    {
        var admin = Admin();
        var created = await CreateAppAsync(admin, Body(await CreateTeamAsync(admin)));

        var all = await ListAllAsync(admin);

        all.Should().ContainSingle(a => a.Id == created.Id);
    }

    [Fact]
    public async Task GetApplications_UserNotInOwningTeam_ExcludesApplicationAndGetByIdReturns404()
    {
        var admin = Admin();
        var created = await CreateAppAsync(admin, Body(await CreateTeamAsync(admin)));
        var outsider = As(NewOid(), UserRole.Responder);

        var all = await ListAllAsync(outsider);
        var byId = await outsider.GetAsync($"{Route}/{created.Id}");

        all.Should().NotContain(a => a.Id == created.Id);
        byId.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetApplications_TeamMember_IncludesApplication()
    {
        var admin = Admin();
        var created = await CreateAppAsync(admin, Body(await CreateTeamAsync(admin)));
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, created.Id);
        var member = As(oid, UserRole.Reporter);

        var all = await ListAllAsync(member);
        var byId = await member.GetAsync($"{Route}/{created.Id}");

        all.Should().ContainSingle(a => a.Id == created.Id);
        byId.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetApplications_Inactive_HiddenByDefaultAndShownWithIncludeInactiveForAdminOnly()
    {
        var admin = Admin();
        var inactive = await CreateAppAsync(admin, Body(await CreateTeamAsync(admin), isActive: false));
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, inactive.Id);
        var member = As(oid, UserRole.Responder);

        (await ListAllAsync(admin)).Should().NotContain(a => a.Id == inactive.Id);
        (await ListAllAsync(admin, includeInactive: true)).Should().ContainSingle(a => a.Id == inactive.Id);
        (await ListAllAsync(member, includeInactive: true)).Should().NotContain(a => a.Id == inactive.Id);
        (await member.GetAsync($"{Route}/{inactive.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetApplications_PagedWithLimit_FollowsCursorWithoutDuplicates()
    {
        var admin = Admin();
        var teamId = await CreateTeamAsync(admin);
        var prefix = Unique("page");
        var created = new[]
        {
            await CreateAppAsync(admin, Body(teamId, name: $"{prefix}-a")),
            await CreateAppAsync(admin, Body(teamId, name: $"{prefix}-b")),
            await CreateAppAsync(admin, Body(teamId, name: $"{prefix}-c")),
        };

        var first = (await admin.GetFromJsonAsync<PageResponse>($"{Route}?limit=2"))!;
        var all = await ListAllAsync(admin, limit: 2);

        first.Items.Should().HaveCount(2);
        first.NextCursor.Should().NotBeNullOrWhiteSpace();
        all.Select(a => a.Id).Should().OnlyHaveUniqueItems();
        all.Where(a => a.Name.StartsWith(prefix)).Select(a => a.Id).Should().Equal(created.Select(a => a.Id));
    }

    [Fact]
    public async Task GetApplicationById_UnknownId_Returns404()
    {
        var response = await Admin().GetAsync($"{Route}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertProblemAsync(response, 404);
    }

    [Fact]
    public async Task PostApplications_DuplicateCodeDifferentCase_Returns409()
    {
        var admin = Admin();
        var teamId = await CreateTeamAsync(admin);
        var code = NewCode();
        await CreateAppAsync(admin, Body(teamId, code));

        var response = await admin.PostAsJsonAsync(Route, Body(teamId, code.ToLowerInvariant()));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertProblemAsync(response, 409);
    }

    [Fact]
    public async Task PostApplications_UnknownTeam_Returns422OnOwningTeamId()
    {
        var response = await Admin().PostAsJsonAsync(Route, Body(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorKeysAsync(response)).Should().Equal("owningTeamId");
    }

    [Fact]
    public async Task PostApplications_UnknownEscalationContact_Returns422OnEscalationContactId()
    {
        var admin = Admin();
        var teamId = await CreateTeamAsync(admin);

        var response = await admin.PostAsJsonAsync(Route, Body(teamId, contact: Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorKeysAsync(response)).Should().Equal("escalationContactId");
    }

    [Fact]
    public async Task PostApplications_InvalidBody_Returns422WithCamelCaseKeys()
    {
        var response = await Admin().PostAsJsonAsync(Route, new { name = " ", code = "no spaces", environments = Array.Empty<string>() });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorKeysAsync(response)).Should().BeEquivalentTo("name", "code", "owningTeamId", "environments");
    }

    [Fact]
    public async Task PostApplications_UnknownEnvironmentName_Returns400()
    {
        var teamId = await CreateTeamAsync(Admin());

        var response = await Admin().PostAsJsonAsync(Route, Body(teamId, environments: ["Staging"]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostApplications_SameIdempotencyKey_CreatesOnce()
    {
        var admin = Admin();
        var teamId = await CreateTeamAsync(admin);
        var body = Body(teamId);
        var key = Guid.NewGuid().ToString();

        async Task<HttpResponseMessage> SendAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = JsonContent.Create(body) };
            request.Headers.Add("Idempotency-Key", key);
            return await admin.SendAsync(request);
        }

        var first = await SendAsync();
        var second = await SendAsync();

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstApp = (await first.Content.ReadFromJsonAsync<AppResponse>())!;
        (await second.Content.ReadFromJsonAsync<AppResponse>()).Should().BeEquivalentTo(firstApp);
        (await ListAllAsync(admin)).Count(a => a.Id == firstApp.Id).Should().Be(1);
    }

    [Fact]
    public async Task PutApplications_CurrentIfMatch_Returns200WithNewRowVersion()
    {
        var admin = Admin();
        var teamId = await CreateTeamAsync(admin);
        var created = await CreateAppAsync(admin, Body(teamId));
        var newName = Unique("renamed");

        var response = await PutAsync(admin, created.Id, Body(teamId, created.Code, newName, environments: ["Development"], isActive: false), created.RowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<AppResponse>())!;
        updated.Name.Should().Be(newName);
        updated.Environments.Should().Equal("Development");
        updated.IsActive.Should().BeFalse();
        updated.RowVersion.Should().NotBe(created.RowVersion);
        response.Headers.ETag!.Tag.Should().Be($"\"{updated.RowVersion}\"");
    }

    [Fact]
    public async Task PutApplications_SameCodeAsItself_Returns200()
    {
        var admin = Admin();
        var teamId = await CreateTeamAsync(admin);
        var created = await CreateAppAsync(admin, Body(teamId));

        var response = await PutAsync(admin, created.Id, Body(teamId, created.Code.ToLowerInvariant()), created.RowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PutApplications_CodeOfAnotherApp_Returns409()
    {
        var admin = Admin();
        var teamId = await CreateTeamAsync(admin);
        var other = await CreateAppAsync(admin, Body(teamId));
        var created = await CreateAppAsync(admin, Body(teamId));

        var response = await PutAsync(admin, created.Id, Body(teamId, other.Code), created.RowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PutApplications_StaleIfMatch_Returns409()
    {
        var admin = Admin();
        var teamId = await CreateTeamAsync(admin);
        var created = await CreateAppAsync(admin, Body(teamId));
        (await PutAsync(admin, created.Id, Body(teamId, created.Code), created.RowVersion)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await PutAsync(admin, created.Id, Body(teamId, created.Code), created.RowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertProblemAsync(response, 409);
    }

    [Fact]
    public async Task PutApplications_MissingIfMatch_Returns428()
    {
        var admin = Admin();
        var teamId = await CreateTeamAsync(admin);
        var created = await CreateAppAsync(admin, Body(teamId));

        var response = await PutAsync(admin, created.Id, Body(teamId, created.Code), rowVersion: null);

        response.StatusCode.Should().Be((HttpStatusCode)428);
    }

    [Fact]
    public async Task PutApplications_UnknownId_AdminReturns404()
    {
        var teamId = await CreateTeamAsync(Admin());
        var rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var response = await PutAsync(Admin(), Guid.NewGuid(), Body(teamId), rowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutApplications_UnknownTeam_Returns422()
    {
        var admin = Admin();
        var created = await CreateAppAsync(admin, Body(await CreateTeamAsync(admin)));

        var response = await PutAsync(admin, created.Id, Body(Guid.NewGuid(), created.Code), created.RowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorKeysAsync(response)).Should().Equal("owningTeamId");
    }

    [Fact]
    public async Task PutApplications_TeamLeadOnOwnApp_Returns200()
    {
        var admin = Admin();
        var teamId = await CreateTeamAsync(admin);
        var created = await CreateAppAsync(admin, Body(teamId));
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, created.Id);

        var response = await PutAsync(As(oid, UserRole.TeamLead), created.Id, Body(teamId, created.Code, Unique("lead-edit")), created.RowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PutApplications_TeamLeadOnOtherApp_Returns403()
    {
        var admin = Admin();
        var teamId = await CreateTeamAsync(admin);
        var created = await CreateAppAsync(admin, Body(teamId));

        var response = await PutAsync(As(NewOid(), UserRole.TeamLead), created.Id, Body(teamId, created.Code), created.RowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertProblemAsync(response, 403);
    }

    [Fact]
    public async Task PutApplications_TeamLeadChangesOwningTeam_Returns403()
    {
        var admin = Admin();
        var created = await CreateAppAsync(admin, Body(await CreateTeamAsync(admin)));
        var otherTeamId = await CreateTeamAsync(admin);
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, created.Id);

        var response = await PutAsync(As(oid, UserRole.TeamLead), created.Id, Body(otherTeamId, created.Code), created.RowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutApplications_AdminChangesOwningTeam_Returns200()
    {
        var admin = Admin();
        var created = await CreateAppAsync(admin, Body(await CreateTeamAsync(admin)));
        var otherTeamId = await CreateTeamAsync(admin);

        var response = await PutAsync(admin, created.Id, Body(otherTeamId, created.Code), created.RowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AppResponse>())!.OwningTeam.Id.Should().Be(otherTeamId);
    }

    [Theory]
    [InlineData(UserRole.Reporter)]
    [InlineData(UserRole.Responder)]
    public async Task PutApplications_ReporterOrResponder_Returns403(UserRole role)
    {
        var admin = Admin();
        var teamId = await CreateTeamAsync(admin);
        var created = await CreateAppAsync(admin, Body(teamId));
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, created.Id);

        var response = await PutAsync(As(oid, role), created.Id, Body(teamId, created.Code), created.RowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
