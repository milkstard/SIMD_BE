using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Incidents.Events;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static IncidentHub.Api.IntegrationTests.Incidents.IncidentWriteHelpers;

namespace IncidentHub.Api.IntegrationTests.Incidents;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CommentTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    private static string NewOid() => Guid.NewGuid().ToString();

    private sealed record Scenario(HttpClient Client, Guid AppId, Guid Id, string Number, Guid CallerId, string Oid, UserRole Role);

    private async Task<Scenario> SeedAsync(UserRole role = UserRole.Responder, bool closed = false)
    {
        var appId = await factory.CreateAppAsync();
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, appId);
        var callerId = await factory.EnsureUserAsync(oid);
        var (id, number) = await _seeder.AddIncidentAsync(
            appId,
            $"Checkout fails {Guid.NewGuid():N}",
            reporterId: role == UserRole.Reporter ? callerId : null,
            configure: closed ? (incident, actor, at) => incident.TransitionTo(IncidentStatus.Closed, actor, null, at, CloseReason.Invalid) : null);
        return new Scenario(factory.CreateClient().AsUser(factory, oid, role), appId, id, number, callerId, oid, role);
    }

    /// <summary>A second member of the same team acting with <paramref name="role"/>.</summary>
    private async Task<(HttpClient Client, Guid UserId)> TeammateAsync(Scenario s, UserRole role)
    {
        var oid = NewOid();
        await factory.AddToTeamAsync(oid, s.AppId);
        var userId = await factory.EnsureUserAsync(oid);
        return (factory.CreateClient().AsUser(factory, oid, role), userId);
    }

    private static Task<HttpResponseMessage> PostCommentAsync(HttpClient client, Guid incidentId, object body, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/incidents/{incidentId}/comments") { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ListAsync(HttpClient client, Guid incidentId, string query = "") =>
        client.GetAsync($"/api/v1/incidents/{incidentId}/comments{query}");

    /// <summary>Adds comments through the domain at a fixed time so ties on <c>CreatedAt</c> can be tested.</summary>
    private async Task<List<Guid>> SeedCommentsAsync(Scenario s, int count, bool isInternal, DateTimeOffset at)
    {
        var ids = new List<Guid>();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var incident = await db.Incidents.SingleAsync(i => i.Id == s.Id);
        var actor = new Actor(s.CallerId, new HashSet<UserRole> { UserRole.Responder });
        for (var i = 0; i < count; i++)
        {
            var comment = incident.AddComment($"<p>Seeded {i}</p>", isInternal, actor, at);
            db.Comments.Add(comment);
            ids.Add(comment.Id);
        }

        await db.SaveChangesAsync();
        return ids;
    }

    private async Task<(int Comments, int History, int Outbox)> CountsAsync(Guid incidentId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var comments = await db.Comments.CountAsync(c => c.IncidentId == incidentId);
        var history = await db.IncidentHistory.CountAsync(h => h.IncidentId == incidentId);
        return (comments, history, (await OutboxTypesAsync(factory, incidentId)).Count);
    }

    // --- POST ---

    [Fact]
    public async Task Add_PublicByResponder_Returns201WithComment()
    {
        var s = await SeedAsync();

        var response = await PostCommentAsync(s.Client, s.Id, new { body = "<p>Looking into it</p>", isInternal = false });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().Be($"/api/v1/incidents/{s.Id}/comments");
        var body = await JsonAsync(response);
        body.GetProperty("incidentId").GetGuid().Should().Be(s.Id);
        body.GetProperty("author").GetProperty("id").GetGuid().Should().Be(s.CallerId);
        body.GetProperty("body").GetString().Should().Be("<p>Looking into it</p>");
        body.GetProperty("isInternal").GetBoolean().Should().BeFalse();
        body.GetProperty("attachments").GetArrayLength().Should().Be(0);
        body.GetProperty("editedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Add_MissingIsInternal_IsPublic()
    {
        var s = await SeedAsync(UserRole.Reporter);

        var response = await PostCommentAsync(s.Client, s.Id, new { body = "<p>Still broken</p>" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await JsonAsync(response)).GetProperty("isInternal").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Add_ScriptInBody_IsStripped()
    {
        var s = await SeedAsync();

        var response = await PostCommentAsync(s.Client, s.Id, new { body = "<p>Hi</p><script>alert(1)</script><img src=\"x.png\" onerror=\"alert(2)\">" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await JsonAsync(response)).GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Comments.AsNoTracking().SingleAsync(c => c.Id == id);
        stored.Body.Should().Contain("<p>Hi</p>").And.NotContain("script").And.NotContain("alert").And.NotContain("onerror");
    }

    [Fact]
    public async Task Add_InternalAsReporter_Returns403AndWritesNothing()
    {
        var s = await SeedAsync(UserRole.Reporter);
        var before = await CountsAsync(s.Id);

        var response = await PostCommentAsync(s.Client, s.Id, new { body = "<p>secret</p>", isInternal = true });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(response)).GetProperty("title").GetString().Should().Be("Internal comment not permitted");
        (await CountsAsync(s.Id)).Should().Be(before);
    }

    [Theory]
    [InlineData(UserRole.Responder)]
    [InlineData(UserRole.TeamLead)]
    [InlineData(UserRole.Admin)]
    public async Task Add_InternalAsResponderRoles_Returns201(UserRole role)
    {
        var s = await SeedAsync(role);

        var response = await PostCommentAsync(s.Client, s.Id, new { body = "<p>Root cause</p>", isInternal = true });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await JsonAsync(response)).GetProperty("isInternal").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<script>alert(1)</script>")]
    public async Task Add_BodyEmptyOrEmptyAfterSanitizing_Returns422(string text)
    {
        var s = await SeedAsync();

        var response = await PostCommentAsync(s.Client, s.Id, new { body = text });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("errors").TryGetProperty("body", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Add_BodyTooLong_Returns422()
    {
        var s = await SeedAsync();

        var response = await PostCommentAsync(s.Client, s.Id, new { body = new string('a', Comment.BodyMaxLength + 1) });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Add_BodyLongerThanLimitOnceSanitized_Returns422()
    {
        var s = await SeedAsync();

        // 9,000 raw characters pass the validator, but each < is stored as &lt;.
        var response = await PostCommentAsync(s.Client, s.Id, new { body = new string('<', 9_000) });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("errors").TryGetProperty("body", out _).Should().BeTrue();
        (await CountsAsync(s.Id)).Comments.Should().Be(0);
    }

    [Fact]
    public async Task Add_BodyOverRequestCap_Returns413()
    {
        var s = await SeedAsync();

        // The in-memory test server only enforces the cap through the idempotency filter, as in the report test.
        var response = await PostCommentAsync(s.Client, s.Id, new { body = new string('a', 200 * 1024) }, Guid.NewGuid().ToString());

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await CountsAsync(s.Id)).Comments.Should().Be(0);
    }

    [Fact]
    public async Task Add_WritesCommentHistoryAndOutboxInOneSave()
    {
        var s = await SeedAsync();
        var before = await CountsAsync(s.Id);

        var response = await PostCommentAsync(s.Client, s.Id, new { body = "<p>Looking</p>" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var commentId = (await JsonAsync(response)).GetProperty("id").GetGuid();
        var after = await CountsAsync(s.Id);
        after.Should().Be((before.Comments + 1, before.History + 1, before.Outbox + 1));
        var row = (await HistoryAsync(factory, s.Id)).Last();
        row.Field.Should().Be(Incident.CommentHistoryField);
        row.NewValue.Should().Be(commentId.ToString("D"));
        row.ActorId.Should().Be(s.CallerId);
        (await OutboxTypesAsync(factory, s.Id)).Should().Contain(nameof(CommentAdded));
    }

    [Fact]
    public async Task Add_OutboxPayload_HoldsIdsNotTheBody()
    {
        var s = await SeedAsync();

        await PostCommentAsync(s.Client, s.Id, new { body = "<p>unique-body-marker</p>" });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payload = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == nameof(CommentAdded) && m.Payload.Contains(s.Id.ToString()))
            .Select(m => m.Payload)
            .SingleAsync();
        payload.Should().NotContain("unique-body-marker");
    }

    [Fact]
    public async Task Add_DoesNotChangeIncidentETag()
    {
        var s = await SeedAsync();
        var etag = await ETagAsync(s.Client, s.Number);

        (await PostCommentAsync(s.Client, s.Id, new { body = "<p>x</p>" })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await ETagAsync(s.Client, s.Number)).Should().Be(etag);
    }

    [Fact]
    public async Task Add_SameIdempotencyKey_CreatesOne()
    {
        var s = await SeedAsync();
        var key = Guid.NewGuid().ToString();
        var body = new { body = "<p>once</p>" };

        var first = await PostCommentAsync(s.Client, s.Id, body, key);
        var second = await PostCommentAsync(s.Client, s.Id, body, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        (await JsonAsync(second)).GetProperty("id").GetGuid().Should().Be((await JsonAsync(first)).GetProperty("id").GetGuid());
        (await CountsAsync(s.Id)).Comments.Should().Be(1);
    }

    [Fact]
    public async Task Add_SameKeyDifferentBody_Returns422()
    {
        var s = await SeedAsync();
        var key = Guid.NewGuid().ToString();
        (await PostCommentAsync(s.Client, s.Id, new { body = "<p>a</p>" }, key)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await PostCommentAsync(s.Client, s.Id, new { body = "<p>b</p>" }, key);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CountsAsync(s.Id)).Comments.Should().Be(1);
    }

    [Fact]
    public async Task Add_NonMember_Returns404()
    {
        var s = await SeedAsync();
        var outsider = factory.CreateClient().AsUser(factory, NewOid(), UserRole.Admin);

        var response = await PostCommentAsync(outsider, s.Id, new { body = "<p>x</p>" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CountsAsync(s.Id)).Comments.Should().Be(0);
    }

    [Fact]
    public async Task Add_UnknownIncident_Returns404()
    {
        var s = await SeedAsync();

        (await PostCommentAsync(s.Client, Guid.NewGuid(), new { body = "<p>x</p>" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Add_OnClosedIncident_Returns201()
    {
        var s = await SeedAsync(closed: true);

        (await PostCommentAsync(s.Client, s.Id, new { body = "<p>Post-mortem</p>" })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Add_NoToken_Returns401()
    {
        var response = await PostCommentAsync(factory.CreateClient(), Guid.NewGuid(), new { body = "<p>x</p>" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Add_FailingSave_WritesCommentHistoryAndOutboxNotAtAll()
    {
        var s = await SeedAsync();
        var before = await CountsAsync(s.Id);
        var unknownAuthor = new Actor(Guid.NewGuid(), new HashSet<UserRole> { UserRole.Responder });

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var incident = await db.Incidents.SingleAsync(i => i.Id == s.Id);
            db.Comments.Add(incident.AddComment("<p>x</p>", false, unknownAuthor, DateTimeOffset.UtcNow));

            // The author row does not exist, so the foreign key fails the whole save.
            var save = () => db.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateException>();
        }

        (await CountsAsync(s.Id)).Should().Be(before);
    }

    // --- GET ---

    [Fact]
    public async Task List_AsReporter_HidesInternalComments()
    {
        var s = await SeedAsync(UserRole.Reporter);
        var (responder, _) = await TeammateAsync(s, UserRole.Responder);
        await PostCommentAsync(responder, s.Id, new { body = "<p>public</p>" });
        await PostCommentAsync(responder, s.Id, new { body = "<p>internal</p>", isInternal = true });

        var response = await ListAsync(s.Client, s.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await JsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
        items.Should().ContainSingle();
        items[0].GetProperty("isInternal").GetBoolean().Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().NotContain("internal</p>");
    }

    [Fact]
    public async Task List_AsReporter_TotalCountExcludesInternal()
    {
        var s = await SeedAsync(UserRole.Reporter);
        await SeedCommentsAsync(s, 2, isInternal: true, DateTimeOffset.UtcNow);
        await SeedCommentsAsync(s, 1, isInternal: false, DateTimeOffset.UtcNow);

        var body = await JsonAsync(await ListAsync(s.Client, s.Id, "?includeTotal=true"));

        body.GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task List_AsResponder_ShowsInternalComments()
    {
        var s = await SeedAsync();
        await PostCommentAsync(s.Client, s.Id, new { body = "<p>public</p>" });
        await PostCommentAsync(s.Client, s.Id, new { body = "<p>internal</p>", isInternal = true });

        var items = (await JsonAsync(await ListAsync(s.Client, s.Id))).GetProperty("items").EnumerateArray().ToList();

        items.Should().HaveCount(2);
        items.Select(i => i.GetProperty("isInternal").GetBoolean()).Should().BeEquivalentTo([false, true]);
        items[0].GetProperty("author").GetProperty("displayName").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task List_OldestFirst()
    {
        var s = await SeedAsync();
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        for (var i = 0; i < 3; i++)
        {
            await SeedCommentsAsync(s, 1, false, start.AddMinutes(i));
        }

        var items = (await JsonAsync(await ListAsync(s.Client, s.Id))).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("createdAt").GetDateTimeOffset()).ToList();

        items.Should().BeInAscendingOrder().And.HaveCount(3);
    }

    [Fact]
    public async Task List_Paging_IsStableWithEqualTimestamps()
    {
        var s = await SeedAsync();
        var seeded = await SeedCommentsAsync(s, 5, false, DateTimeOffset.UtcNow.AddMinutes(-5));

        var seen = new List<Guid>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var query = $"?limit=2{(cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}")}";
            var body = await JsonAsync(await ListAsync(s.Client, s.Id, query));
            seen.AddRange(body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
            cursor = body.GetProperty("nextCursor").GetString();
            if (cursor is null)
            {
                break;
            }
        }

        seen.Should().HaveCount(5).And.OnlyHaveUniqueItems().And.BeEquivalentTo(seeded);
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?cursor=not-a-cursor")]
    public async Task List_BadLimitOrCursor_Returns422(string query)
    {
        var s = await SeedAsync();

        (await ListAsync(s.Client, s.Id, query)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task List_NonMemberOrUnknown_Returns404()
    {
        var s = await SeedAsync();
        var outsider = factory.CreateClient().AsUser(factory, NewOid(), UserRole.Admin);

        (await ListAsync(outsider, s.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListAsync(s.Client, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_NoToken_Returns401()
    {
        (await ListAsync(factory.CreateClient(), Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // --- commentCount ---

    [Fact]
    public async Task CommentCount_InDetailAndList_ExcludesInternalForReporter()
    {
        var s = await SeedAsync(UserRole.Reporter);
        var (responder, _) = await TeammateAsync(s, UserRole.Responder);
        await SeedCommentsAsync(s, 2, isInternal: false, DateTimeOffset.UtcNow);
        await SeedCommentsAsync(s, 3, isInternal: true, DateTimeOffset.UtcNow);

        var reporterDetail = await JsonAsync(await s.Client.GetAsync(Route(s.Number)));
        var responderDetail = await JsonAsync(await responder.GetAsync(Route(s.Number)));
        var reporterList = await JsonAsync(await s.Client.GetAsync($"/api/v1/incidents?applicationId={s.AppId}"));
        var responderList = await JsonAsync(await responder.GetAsync($"/api/v1/incidents?applicationId={s.AppId}"));

        reporterDetail.GetProperty("commentCount").GetInt32().Should().Be(2);
        responderDetail.GetProperty("commentCount").GetInt32().Should().Be(5);
        reporterList.GetProperty("items")[0].GetProperty("commentCount").GetInt32().Should().Be(2);
        responderList.GetProperty("items")[0].GetProperty("commentCount").GetInt32().Should().Be(5);
    }
}
