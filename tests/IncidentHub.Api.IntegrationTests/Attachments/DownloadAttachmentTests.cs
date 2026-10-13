using System.Net;
using System.Web;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static IncidentHub.Api.IntegrationTests.Attachments.AttachmentTestSupport;

namespace IncidentHub.Api.IntegrationTests.Attachments;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DownloadAttachmentTests(ApiFactory factory)
{
    private readonly IncidentSeeder _seeder = new(factory);

    private static string DownloadUrl(Guid id) => $"/api/v1/attachments/{id}/download";

    private async Task<(HttpClient Client, Guid UserId, string Oid)> NewUserAsync(UserRole role = UserRole.Responder, Guid? appId = null)
    {
        var oid = NewOid();
        if (appId is not null)
        {
            await factory.AddToTeamAsync(oid, appId.Value);
        }

        var userId = await factory.EnsureUserAsync(oid);
        return (NewClient(factory, oid, role), userId, oid);
    }

    /// <summary>An incident of a new team, a member of that team acting as <paramref name="role"/>, and a clean attachment linked to it.</summary>
    private async Task<(Guid AppId, Guid IncidentId, Guid AuthorId, Attachment Attachment)> LinkedScenarioAsync(
        bool internalComment = false,
        bool linkComment = false)
    {
        var appId = await factory.CreateAppAsync();
        var (incidentId, _) = await _seeder.AddIncidentAsync(appId, $"Checkout fails {Guid.NewGuid():N}");
        var authorId = await factory.EnsureUserAsync(NewOid());
        var attachment = await SeedAsync(factory, authorId, ScanStatus.Clean);
        Guid? commentId = linkComment ? await AddCommentAsync(factory, incidentId, authorId, internalComment) : null;
        await LinkAsync(factory, attachment.Id, incidentId, commentId);
        return (appId, incidentId, authorId, attachment);
    }

    [Fact]
    public async Task Download_Pending_Returns409()
    {
        var (client, userId, _) = await NewUserAsync();
        var attachment = await SeedAsync(factory, userId, ScanStatus.Pending);

        var response = await client.GetAsync(DownloadUrl(attachment.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Attachment not available").And.Contain("Pending");
    }

    [Fact]
    public async Task Download_Infected_Returns409()
    {
        var (client, userId, _) = await NewUserAsync();
        var attachment = await SeedAsync(factory, userId, ScanStatus.Infected);

        var response = await client.GetAsync(DownloadUrl(attachment.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Infected");
    }

    [Fact]
    public async Task Download_Clean_Returns302WithFiveMinuteSas()
    {
        var (client, userId, _) = await NewUserAsync();
        var attachment = await SeedAsync(factory, userId, ScanStatus.Clean);
        var before = DateTimeOffset.UtcNow;

        var response = await client.GetAsync(DownloadUrl(attachment.Id));
        var after = DateTimeOffset.UtcNow;

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var location = response.Headers.Location!;
        var query = HttpUtility.ParseQueryString(location.Query);
        query["sp"].Should().Be("r");
        var expires = DateTimeOffset.Parse(query["se"]!, null, System.Globalization.DateTimeStyles.AssumeUniversal);
        expires.Should().BeOnOrAfter(before.AddMinutes(5).AddSeconds(-2)).And.BeOnOrBefore(after.AddMinutes(5).AddSeconds(2));
    }

    [Fact]
    public async Task Download_Clean_SasLinkServesTheFileAsAnAttachment()
    {
        var (client, userId, _) = await NewUserAsync();
        var attachment = await SeedAsync(factory, userId, ScanStatus.Clean, fileName: "résumé shot.png");

        var redirect = await client.GetAsync(DownloadUrl(attachment.Id));
        using var plain = new HttpClient();
        var file = await plain.GetAsync(redirect.Headers.Location);

        file.StatusCode.Should().Be(HttpStatusCode.OK);
        (await file.Content.ReadAsByteArrayAsync()).Should().Equal(Png);
        file.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        var disposition = string.Join(' ', file.Content.Headers.GetValues("Content-Disposition"));
        disposition.Should().Contain("attachment").And.Contain("filename*=UTF-8''r%C3%A9sum%C3%A9%20shot.png");
    }

    [Fact]
    public async Task Download_Clean_SasLinkIsReadOnly()
    {
        var (client, userId, _) = await NewUserAsync();
        var attachment = await SeedAsync(factory, userId, ScanStatus.Clean);
        var redirect = await client.GetAsync(DownloadUrl(attachment.Id));

        using var plain = new HttpClient();
        var put = new HttpRequestMessage(HttpMethod.Put, redirect.Headers.Location) { Content = new ByteArrayContent([1, 2, 3]) };
        put.Headers.Add("x-ms-blob-type", "BlockBlob");
        var response = await plain.SendAsync(put);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Download_LogFile_IsServedAsOpaqueBytes()
    {
        var (client, userId, _) = await NewUserAsync();
        var attachment = await SeedAsync(factory, userId, ScanStatus.Clean, "<html>hi</html>"u8.ToArray(), "page.log", "text/plain");
        var redirect = await client.GetAsync(DownloadUrl(attachment.Id));

        using var plain = new HttpClient();
        var file = await plain.GetAsync(redirect.Headers.Location);

        file.Content.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task Download_SasLink_ServesTheBlobOnce_ThenExpires()
    {
        var (_, userId, _) = await NewUserAsync();
        var attachment = await SeedAsync(factory, userId, ScanStatus.Clean);
        using var scope = factory.Services.CreateScope();
        var uri = await scope.ServiceProvider.GetRequiredService<IFileStore>()
            .CreateDownloadUriAsync(attachment.BlobPath, "shot.png", "image/png", TimeSpan.FromSeconds(3), CancellationToken.None);

        using var plain = new HttpClient();
        (await plain.GetAsync(uri)).StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(TimeSpan.FromSeconds(5));
        (await plain.GetAsync(uri)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Download_UnknownId_Returns404()
    {
        var (client, _, _) = await NewUserAsync();

        var response = await client.GetAsync(DownloadUrl(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_NoToken_Returns401()
    {
        var anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await anonymous.GetAsync(DownloadUrl(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Download_UnlinkedUploadOfSomeoneElse_Returns404()
    {
        var (_, uploaderId, _) = await NewUserAsync();
        var (other, _, _) = await NewUserAsync();
        var attachment = await SeedAsync(factory, uploaderId, ScanStatus.Clean);

        var response = await other.GetAsync(DownloadUrl(attachment.Id));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_UnlinkedUploadOfSomeoneElseStillPending_Returns404NotConflict()
    {
        var (_, uploaderId, _) = await NewUserAsync();
        var (other, _, _) = await NewUserAsync();
        var attachment = await SeedAsync(factory, uploaderId, ScanStatus.Pending);

        var response = await other.GetAsync(DownloadUrl(attachment.Id));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_LinkedAttachmentToTeamMember_Returns302()
    {
        var s = await LinkedScenarioAsync();
        var (member, _, _) = await NewUserAsync(UserRole.Reporter, s.AppId);

        var response = await member.GetAsync(DownloadUrl(s.Attachment.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Download_OtherTeamsLinkedAttachment_Returns404()
    {
        var s = await LinkedScenarioAsync();
        var otherAppId = await factory.CreateAppAsync();
        var (outsider, _, _) = await NewUserAsync(UserRole.Admin, otherAppId);

        var response = await outsider.GetAsync(DownloadUrl(s.Attachment.Id));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_LinkedAttachmentToUploaderOutsideTheTeam_Returns404()
    {
        // Once linked, the uploader's own access follows the incident's team, not the upload.
        var s = await LinkedScenarioAsync();
        var uploaderClient = NewClient(factory, await OidOfAsync(s.AuthorId), UserRole.Responder);

        var response = await uploaderClient.GetAsync(DownloadUrl(s.Attachment.Id));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_InternalCommentAttachment_ReporterGets404ResponderGets302()
    {
        var s = await LinkedScenarioAsync(internalComment: true, linkComment: true);
        var (reporter, _, _) = await NewUserAsync(UserRole.Reporter, s.AppId);
        var (responder, _, _) = await NewUserAsync(UserRole.Responder, s.AppId);

        (await reporter.GetAsync(DownloadUrl(s.Attachment.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await responder.GetAsync(DownloadUrl(s.Attachment.Id))).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Download_PublicCommentAttachment_ReporterGets302()
    {
        var s = await LinkedScenarioAsync(internalComment: false, linkComment: true);
        var (reporter, _, _) = await NewUserAsync(UserRole.Reporter, s.AppId);

        var response = await reporter.GetAsync(DownloadUrl(s.Attachment.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    /// <summary>The Entra object id of an existing user, to act as them again.</summary>
    private async Task<string> OidOfAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.EntraObjectId).SingleAsync();
    }
}
