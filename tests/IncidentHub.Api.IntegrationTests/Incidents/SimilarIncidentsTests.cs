using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Users;

namespace IncidentHub.Api.IntegrationTests.Incidents;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SimilarIncidentsTests(ApiFactory factory)
{
    private const string Route = "/api/v1/incidents/similar";
    private const string Unavailable = "Application not found or not available to you.";

    private readonly IncidentSeeder _seeder = new(factory);

    private static string NewOid() => Guid.NewGuid().ToString();

    private async Task<(HttpClient Client, Guid AppId)> MemberAsync(UserRole role = UserRole.Reporter)
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        return (factory.CreateClient().AsUser(factory, oid, role), appId);
    }

    private static string Url(Guid? appId, string? title)
    {
        var query = new List<string>();
        if (appId is not null)
        {
            query.Add($"applicationId={appId}");
        }

        if (title is not null)
        {
            query.Add($"title={Uri.EscapeDataString(title)}");
        }

        return $"{Route}?{string.Join('&', query)}";
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<JsonElement> WaitForResultsAsync(HttpClient client, Guid appId, string title, int expectedCount)
    {
        JsonElement items = default;
        await IncidentSeeder.EventuallyAsync(async () =>
        {
            var response = await client.GetAsync(Url(appId, title));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            items = await JsonAsync(response);
            return items.GetArrayLength() >= expectedCount;
        });
        return items;
    }

    [Fact]
    public async Task Similar_OpenMatches_Returns200WithSummaries()
    {
        var (client, appId) = await MemberAsync();
        var token = $"tok{Guid.NewGuid():N}";
        var exact = await _seeder.AddAsync(appId, token);
        var longer = await _seeder.AddAsync(appId, $"Payment {token} gateway timeout");

        var items = await WaitForResultsAsync(client, appId, token, expectedCount: 2);

        items.GetArrayLength().Should().Be(2);
        // Order is by full-text rank, which is coarse (ties fall back to newest first), so only membership is asserted here.
        items.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().BeEquivalentTo([exact, longer]);
        var item = items.EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == exact);
        item.GetProperty("number").GetString().Should().StartWith("INC-");
        item.GetProperty("application").GetProperty("id").GetGuid().Should().Be(appId);
        item.GetProperty("status").GetString().Should().Be("New");
        item.GetProperty("assignee").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("rowVersion").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Similar_OnlyClosedMatches_ReturnsEmpty()
    {
        var (client, appId) = await MemberAsync();
        var token = $"tok{Guid.NewGuid():N}";
        await _seeder.AddAsync(appId, $"Checkout {token}", closed: true);
        var control = await _seeder.AddAsync(appId, $"Control {token}");
        await _seeder.WaitForFullTextPopulationAsync(TimeSpan.FromSeconds(45));

        var items = await WaitForResultsAsync(client, appId, token, expectedCount: 1);

        items.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(control);
    }

    [Fact]
    public async Task Similar_ShortTitle_ReturnsEmpty()
    {
        var (client, appId) = await MemberAsync();

        var response = await client.GetAsync(Url(appId, "ab"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(response)).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Similar_MissingParameters_Returns422WithFieldErrors()
    {
        var (client, _) = await MemberAsync();

        var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var errors = (await JsonAsync(response)).GetProperty("errors");
        errors.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("applicationId", "title");
    }

    [Fact]
    public async Task Similar_TitleTooLong_Returns422OnTitle()
    {
        var (client, appId) = await MemberAsync();

        var response = await client.GetAsync(Url(appId, new string('a', 201)));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Equal("title");
    }

    [Fact]
    public async Task Similar_NonGuidApplicationId_Returns400()
    {
        var (client, _) = await MemberAsync();

        var response = await client.GetAsync($"{Route}?applicationId=not-a-guid&title=Checkout");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Similar_NonMember_Returns422()
    {
        var appId = await factory.CreateAppAsync();
        var client = factory.CreateClient().AsUser(factory, NewOid(), UserRole.Responder);

        var response = await client.GetAsync(Url(appId, "Checkout fails"));

        await AssertApplicationUnavailableAsync(response);
    }

    [Fact]
    public async Task Similar_AdminNotMember_Returns422()
    {
        var appId = await factory.CreateAppAsync();
        var client = factory.CreateClient().AsUser(factory, NewOid(), UserRole.Admin);

        var response = await client.GetAsync(Url(appId, "Checkout fails"));

        await AssertApplicationUnavailableAsync(response);
    }

    [Fact]
    public async Task Similar_UnknownApplication_Returns422()
    {
        var (client, _) = await MemberAsync();

        var response = await client.GetAsync(Url(Guid.NewGuid(), "Checkout fails"));

        await AssertApplicationUnavailableAsync(response);
    }

    [Fact]
    public async Task Similar_NoToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(Url(Guid.NewGuid(), "Checkout fails"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Similar_NoRole_Returns403()
    {
        var client = factory.CreateClient().AsUser(factory, NewOid());

        var response = await client.GetAsync(Url(Guid.NewGuid(), "Checkout fails"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task AssertApplicationUnavailableAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("errors").GetProperty("applicationId")[0].GetString().Should().Be(Unavailable);
    }
}
