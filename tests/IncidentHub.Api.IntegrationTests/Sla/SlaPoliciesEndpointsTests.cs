using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Common.Sla;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentHub.Api.IntegrationTests.Sla;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SlaPoliciesEndpointsTests(ApiFactory factory)
{
    private const string Route = "/api/v1/sla-policies";

    private static readonly string[] SeverityOrder = ["Critical", "High", "Medium", "Low"];

    private HttpClient Admin() => factory.CreateClient().AsUser(factory, Guid.NewGuid().ToString(), UserRole.Admin);

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string id, object body, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"{Route}/{id}") { Content = JsonContent.Create(body) };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request);
    }

    private static object Body(int ack = 30, int resolve = 120, int percent = 70) =>
        new { ackMinutes = ack, resolveMinutes = resolve, escalateAtPercent = percent };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>The quoted ETag of the policy, read through the endpoint.</summary>
    private static async Task<string> ETagAsync(HttpClient client, string id)
    {
        var response = await client.GetAsync($"{Route}/{id}");
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.Tag;
    }

    /// <summary>Puts the four rows back to the seeded values so other tests of the shared database see the defaults.</summary>
    private async Task RestoreDefaultsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var defaults = new SlaDefaultsOptions();
        foreach (var setting in await db.SlaPolicies.ToListAsync())
        {
            var target = defaults.For(setting.Severity);
            setting.Update(target.AckMinutes, target.ResolveMinutes, target.EscalateAtPercent);
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Migration_SeedsFourDefaultRows()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var defaults = new SlaDefaultsOptions();

        var rows = await db.SlaPolicies.AsNoTracking().ToListAsync();

        rows.Should().HaveCount(4);
        foreach (var row in rows)
        {
            var expected = defaults.For(row.Severity);
            row.AckMinutes.Should().Be(expected.AckMinutes);
            row.ResolveMinutes.Should().Be(expected.ResolveMinutes);
            row.EscalateAtPercent.Should().Be(expected.EscalateAtPercent);
        }
    }

    [Fact]
    public async Task List_Admin_ReturnsFourOrderedBySeverity()
    {
        var response = await Admin().GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await JsonAsync(response)).EnumerateArray().ToList();
        items.Select(i => i.GetProperty("id").GetString()).Should().Equal(SeverityOrder);
        items.Select(i => i.GetProperty("severity").GetString()).Should().Equal(SeverityOrder);
        items[0].GetProperty("ackMinutes").GetInt32().Should().Be(15);
        items[0].GetProperty("resolveMinutes").GetInt32().Should().Be(240);
        items[0].GetProperty("escalateAtPercent").GetInt32().Should().Be(80);
        items[0].GetProperty("rowVersion").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(UserRole.Reporter)]
    [InlineData(UserRole.Responder)]
    [InlineData(UserRole.TeamLead)]
    public async Task List_NonAdmin_Returns403(UserRole role)
    {
        var client = factory.CreateClient().AsUser(factory, Guid.NewGuid().ToString(), role);

        var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_NoToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_NonAdmin_Returns403()
    {
        var client = factory.CreateClient().AsUser(factory, Guid.NewGuid().ToString(), UserRole.TeamLead);

        var response = await client.GetAsync($"{Route}/Critical");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("High")]
    [InlineData("high")]
    public async Task Get_KnownSeverity_Returns200WithETag(string id)
    {
        var response = await Admin().GetAsync($"{Route}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("id").GetString().Should().Be("High");
        response.Headers.ETag!.Tag.Should().Be($"\"{body.GetProperty("rowVersion").GetString()}\"");
    }

    [Theory]
    [InlineData("Bogus")]
    [InlineData("1")]
    [InlineData("Critical,High")]
    public async Task Get_UnknownId_Returns404(string id)
    {
        var response = await Admin().GetAsync($"{Route}/{Uri.EscapeDataString(id)}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_NonAdmin_Returns403()
    {
        var etag = await ETagAsync(Admin(), "Low");
        var client = factory.CreateClient().AsUser(factory, Guid.NewGuid().ToString(), UserRole.TeamLead);

        var response = await PutAsync(client, "Low", Body(), etag);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Put_Valid_UpdatesAndReturnsNewETag()
    {
        var admin = Admin();
        try
        {
            var etag = await ETagAsync(admin, "Low");

            var response = await PutAsync(admin, "Low", Body(ack: 30, resolve: 120, percent: 70), etag);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.ETag!.Tag.Should().NotBe(etag);
            var body = await JsonAsync(response);
            body.GetProperty("id").GetString().Should().Be("Low");
            body.GetProperty("ackMinutes").GetInt32().Should().Be(30);
            body.GetProperty("resolveMinutes").GetInt32().Should().Be(120);
            body.GetProperty("escalateAtPercent").GetInt32().Should().Be(70);

            var reread = await JsonAsync(await admin.GetAsync($"{Route}/Low"));
            reread.GetProperty("ackMinutes").GetInt32().Should().Be(30);
            reread.GetProperty("rowVersion").GetString().Should().Be(body.GetProperty("rowVersion").GetString());
        }
        finally
        {
            await RestoreDefaultsAsync();
        }
    }

    [Fact]
    public async Task Put_StaleIfMatch_Returns409()
    {
        var admin = Admin();
        try
        {
            var stale = await ETagAsync(admin, "Medium");
            (await PutAsync(admin, "Medium", Body(), stale)).StatusCode.Should().Be(HttpStatusCode.OK);

            var response = await PutAsync(admin, "Medium", Body(ack: 31), stale);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await JsonAsync(await admin.GetAsync($"{Route}/Medium"))).GetProperty("ackMinutes").GetInt32().Should().Be(30);
        }
        finally
        {
            await RestoreDefaultsAsync();
        }
    }

    [Fact]
    public async Task Put_NoIfMatch_Returns428()
    {
        var response = await PutAsync(Admin(), "Low", Body(), ifMatch: null);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task Put_MalformedIfMatch_Returns400()
    {
        var response = await PutAsync(Admin(), "Low", Body(), "not-quoted");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_UnknownId_Returns404()
    {
        var admin = Admin();
        var etag = await ETagAsync(admin, "Low");

        var response = await PutAsync(admin, "Bogus", Body(), etag);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_Invalid_Returns422WithCamelCaseKeys()
    {
        var admin = Admin();
        var etag = await ETagAsync(admin, "Low");

        var response = await PutAsync(admin, "Low", Body(ack: 0, resolve: -1, percent: 100), etag);

        response.StatusCode.Should().Be((HttpStatusCode)422);
        var problem = await JsonAsync(response);
        problem.GetProperty("status").GetInt32().Should().Be(422);
        problem.GetProperty("errors").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("ackMinutes", "resolveMinutes", "escalateAtPercent");
        (await JsonAsync(await admin.GetAsync($"{Route}/Low"))).GetProperty("ackMinutes").GetInt32().Should().Be(480);
    }

    [Fact]
    public async Task Put_ResolveBelowAck_Returns422()
    {
        var admin = Admin();
        var etag = await ETagAsync(admin, "Low");

        var response = await PutAsync(admin, "Low", Body(ack: 60, resolve: 59), etag);

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Put_ThenReport_UsesNewMinutes()
    {
        var admin = Admin();
        try
        {
            var etag = await ETagAsync(admin, "Low");
            (await PutAsync(admin, "Low", Body(ack: 7, resolve: 9, percent: 50), etag)).StatusCode.Should().Be(HttpStatusCode.OK);

            var incident = await ReportAsync("Low");

            var createdAt = incident.GetProperty("createdAt").GetDateTimeOffset();
            incident.GetProperty("ackDueAt").GetDateTimeOffset().Should().Be(createdAt.AddMinutes(7));
            incident.GetProperty("resolveDueAt").GetDateTimeOffset().Should().Be(createdAt.AddMinutes(9));
        }
        finally
        {
            await RestoreDefaultsAsync();
        }
    }

    [Fact]
    public async Task Put_DoesNotChangeExistingIncidentDueTimes()
    {
        var admin = Admin();
        try
        {
            var incident = await ReportAsync("High");
            var id = incident.GetProperty("id").GetGuid();
            var before = await DueTimesAsync(id);

            var etag = await ETagAsync(admin, "High");
            (await PutAsync(admin, "High", Body(ack: 5, resolve: 10, percent: 50), etag)).StatusCode.Should().Be(HttpStatusCode.OK);

            (await DueTimesAsync(id)).Should().Be(before);
        }
        finally
        {
            await RestoreDefaultsAsync();
        }
    }

    private async Task<JsonElement> ReportAsync(string severity)
    {
        var appId = await factory.CreateAppAsync();
        var oid = Guid.NewGuid().ToString();
        await factory.AddToTeamAsync(oid, appId);
        var client = factory.CreateClient().AsUser(factory, oid, UserRole.Responder);

        var response = await client.PostAsJsonAsync("/api/v1/incidents", new
        {
            applicationId = appId,
            environment = "Production",
            severity,
            title = $"Checkout fails {Guid.NewGuid():N}",
            description = "<p>Error page after paying.</p>",
            stepsToReproduce = "1. Pay",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await JsonAsync(response);
    }

    private async Task<(DateTimeOffset? Ack, DateTimeOffset? Resolve)> DueTimesAsync(Guid incidentId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var incident = await db.Incidents.AsNoTracking().SingleAsync(i => i.Id == incidentId);
        return (incident.AckDueAt, incident.ResolveDueAt);
    }
}
