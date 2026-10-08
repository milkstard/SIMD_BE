using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Users;

namespace IncidentHub.Api.IntegrationTests.Teams;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TeamsEndpointsTests(ApiFactory factory)
{
    private const string Route = "/api/v1/teams";

    private sealed record TeamResponse(Guid Id, string Name, string Email, string? TeamsChannelUrl, string RowVersion);

    private sealed record PageResponse(List<TeamResponse> Items, string? NextCursor, int? TotalCount);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private HttpClient Admin() => factory.CreateClient().AsUser(factory, Guid.NewGuid().ToString(), UserRole.Admin);

    private static object Body(string? name = null, string? email = null, string? url = null) =>
        new { name = name ?? Unique("team"), email = email ?? "team@acme.com", teamsChannelUrl = url };

    private static async Task<TeamResponse> CreateAsync(HttpClient client, object? body = null)
    {
        var response = await client.PostAsJsonAsync(Route, body ?? Body());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TeamResponse>())!;
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

    private static async Task<List<TeamResponse>> ListAllAsync(HttpClient client, int limit = 100)
    {
        var all = new List<TeamResponse>();
        string? cursor = null;
        do
        {
            var url = $"{Route}?limit={limit}" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
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

    [Theory]
    [InlineData(UserRole.Reporter)]
    [InlineData(UserRole.Responder)]
    [InlineData(UserRole.TeamLead)]
    public async Task PostTeams_NonAdmin_Returns403(UserRole role)
    {
        var client = factory.CreateClient().AsUser(factory, Guid.NewGuid().ToString(), role);

        var response = await client.PostAsJsonAsync(Route, Body());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertProblemAsync(response, 403);
    }

    [Theory]
    [InlineData(UserRole.Reporter)]
    [InlineData(UserRole.TeamLead)]
    public async Task GetTeams_NonAdmin_Returns403(UserRole role)
    {
        var client = factory.CreateClient().AsUser(factory, Guid.NewGuid().ToString(), role);

        var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutTeams_NonAdmin_Returns403()
    {
        var team = await CreateAsync(Admin());
        var client = factory.CreateClient().AsUser(factory, Guid.NewGuid().ToString(), UserRole.TeamLead);

        var response = await PutAsync(client, team.Id, Body(), team.RowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostTeams_NoToken_Returns401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(Route, Body());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostTeams_Admin_Returns201WithLocationAndETag()
    {
        var name = Unique("team");

        var response = await Admin().PostAsJsonAsync(Route, Body(name, "ops@acme.com", "https://teams.example.com/hook"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var team = (await response.Content.ReadFromJsonAsync<TeamResponse>())!;
        response.Headers.Location!.ToString().Should().EndWith($"{Route}/{team.Id}");
        response.Headers.ETag!.Tag.Should().Be($"\"{team.RowVersion}\"");
        team.Name.Should().Be(name);
        team.RowVersion.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetTeamById_AfterCreate_ReturnsSameDataAndETag()
    {
        var client = Admin();
        var created = await CreateAsync(client, Body(url: "https://teams.example.com/hook"));

        var response = await client.GetAsync($"{Route}/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TeamResponse>()).Should().Be(created);
        response.Headers.ETag!.Tag.Should().Be($"\"{created.RowVersion}\"");
    }

    [Fact]
    public async Task GetTeamById_UnknownId_Returns404()
    {
        var response = await Admin().GetAsync($"{Route}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertProblemAsync(response, 404);
    }

    [Fact]
    public async Task GetTeams_AfterCreate_ContainsTeamWithSameData()
    {
        var client = Admin();
        var created = await CreateAsync(client, Body(url: "https://teams.example.com/hook"));

        var all = await ListAllAsync(client);

        all.Should().ContainSingle(t => t.Id == created.Id).Which.Should().Be(created);
    }

    [Fact]
    public async Task GetTeams_PagedWithLimit_ReturnsNextCursorAndFollowsIt()
    {
        var client = Admin();
        var prefix = Unique("page");
        var created = new[]
        {
            await CreateAsync(client, Body($"{prefix}-a")),
            await CreateAsync(client, Body($"{prefix}-b")),
            await CreateAsync(client, Body($"{prefix}-c")),
        };

        var first = (await client.GetFromJsonAsync<PageResponse>($"{Route}?limit=2&includeTotal=true"))!;
        var all = await ListAllAsync(client, limit: 2);

        first.Items.Should().HaveCount(2);
        first.NextCursor.Should().NotBeNullOrWhiteSpace();
        first.TotalCount.Should().BeGreaterOrEqualTo(3);
        all.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        all.Select(t => t.Name).Should().BeInAscendingOrder(StringComparer.Ordinal);
        all.Where(t => t.Name.StartsWith(prefix)).Select(t => t.Id).Should().Equal(created.Select(t => t.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task GetTeams_LimitOutOfRange_Returns422(int limit)
    {
        var response = await Admin().GetAsync($"{Route}?limit={limit}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task GetTeams_InvalidCursor_Returns422OnCursor()
    {
        var response = await Admin().GetAsync($"{Route}?cursor=garbage");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").TryGetProperty("cursor", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PostTeams_InvalidBody_Returns422WithCamelCaseFieldErrors()
    {
        var response = await Admin().PostAsJsonAsync(
            Route,
            new { name = " ", email = "not-an-email", teamsChannelUrl = "http://insecure.example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await AssertProblemAsync(response, 422);
        var errors = problem.GetProperty("errors");
        errors.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("name", "email", "teamsChannelUrl");
    }

    [Fact]
    public async Task PostTeams_MissingFields_Returns422()
    {
        var response = await Admin().PostAsJsonAsync(Route, new { });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        errors.EnumerateObject().Select(p => p.Name).Should().Contain(["name", "email"]);
    }

    [Fact]
    public async Task PostTeams_MalformedJson_Returns400()
    {
        var content = new StringContent("{ nope", System.Text.Encoding.UTF8, "application/json");

        var response = await Admin().PostAsync(Route, content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PutTeams_CurrentIfMatch_Returns200WithNewRowVersion()
    {
        var client = Admin();
        var created = await CreateAsync(client);
        var newName = Unique("renamed");

        var response = await PutAsync(
            client,
            created.Id,
            Body(newName, "new@acme.com", "https://teams.example.com/new"),
            created.RowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<TeamResponse>())!;
        updated.Name.Should().Be(newName);
        updated.Email.Should().Be("new@acme.com");
        updated.TeamsChannelUrl.Should().Be("https://teams.example.com/new");
        updated.RowVersion.Should().NotBe(created.RowVersion);
        response.Headers.ETag!.Tag.Should().Be($"\"{updated.RowVersion}\"");
    }

    [Fact]
    public async Task PutTeams_StaleIfMatch_Returns409WithTraceId()
    {
        var client = Admin();
        var created = await CreateAsync(client);
        (await PutAsync(client, created.Id, Body(), created.RowVersion)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await PutAsync(client, created.Id, Body(), created.RowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertProblemAsync(response, 409);
    }

    [Fact]
    public async Task PutTeams_MissingIfMatch_Returns428()
    {
        var client = Admin();
        var created = await CreateAsync(client);

        var response = await PutAsync(client, created.Id, Body(), rowVersion: null);

        response.StatusCode.Should().Be((HttpStatusCode)428);
        await AssertProblemAsync(response, 428);
    }

    [Fact]
    public async Task PutTeams_MalformedIfMatch_Returns400()
    {
        var client = Admin();
        var created = await CreateAsync(client);

        var response = await PutAsync(client, created.Id, Body(), "***");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PutTeams_UnknownId_Returns404()
    {
        var rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var response = await PutAsync(Admin(), Guid.NewGuid(), Body(), rowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutTeams_InvalidBody_Returns422()
    {
        var client = Admin();
        var created = await CreateAsync(client);

        var response = await PutAsync(
            client,
            created.Id,
            new { name = string.Empty, email = "x", teamsChannelUrl = (string?)null },
            created.RowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private static Task<HttpResponseMessage> PostWithKeyAsync(HttpClient client, object body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task PostTeams_SameIdempotencyKey_ReturnsOriginalAndCreatesOneRow()
    {
        var client = Admin();
        var name = Unique("idem");
        var key = Guid.NewGuid().ToString();

        var first = await PostWithKeyAsync(client, Body(name), key);
        var second = await PostWithKeyAsync(client, Body(name), key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstTeam = (await first.Content.ReadFromJsonAsync<TeamResponse>())!;
        var secondTeam = (await second.Content.ReadFromJsonAsync<TeamResponse>())!;
        secondTeam.Should().Be(firstTeam);
        second.Headers.Location.Should().Be(first.Headers.Location);
        (await ListAllAsync(client)).Count(t => t.Name == name).Should().Be(1);
    }

    [Fact]
    public async Task PostTeams_InvalidIdempotencyKey_Returns400()
    {
        var response = await PostWithKeyAsync(Admin(), Body(), "not-a-uuid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostTeams_FailedRequestWithKey_AllowsRetryWithSameKey()
    {
        var client = Admin();
        var key = Guid.NewGuid().ToString();

        var invalid = await PostWithKeyAsync(client, new { name = string.Empty, email = "x" }, key);
        var retry = await PostWithKeyAsync(client, Body(), key);

        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
