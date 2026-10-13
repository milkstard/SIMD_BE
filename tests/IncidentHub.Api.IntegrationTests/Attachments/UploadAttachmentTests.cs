using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Domain.Incidents;
using IncidentHub.Domain.Users;
using static IncidentHub.Api.IntegrationTests.Attachments.AttachmentTestSupport;

namespace IncidentHub.Api.IntegrationTests.Attachments;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class UploadAttachmentTests(ApiFactory factory)
{
    private async Task<(HttpClient Client, Guid UserId)> NewUserAsync(UserRole role = UserRole.Reporter)
    {
        var oid = NewOid();
        var userId = await factory.EnsureUserAsync(oid);
        return (NewClient(factory, oid, role), userId);
    }

    [Fact]
    public async Task Upload_PngUnder25Mb_Returns201Pending()
    {
        var (client, userId) = await NewUserAsync();

        var response = await UploadAsync(client, Png, "screenshot.png", "image/png");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        body.GetProperty("scanStatus").GetString().Should().Be("Pending");
        body.GetProperty("fileName").GetString().Should().Be("screenshot.png");
        body.GetProperty("contentType").GetString().Should().Be("image/png");
        body.GetProperty("sizeBytes").GetInt64().Should().Be(Png.Length);
        body.GetProperty("uploadedBy").GetProperty("id").GetGuid().Should().Be(userId);
        response.Headers.Location!.ToString().Should().Be($"/api/v1/attachments/{id}/download");

        var row = await LoadAsync(factory, id);
        row.ScanStatus.Should().Be(ScanStatus.Pending);
        row.IncidentId.Should().BeNull();
        row.UploadedById.Should().Be(userId);
        row.BlobPath.Should().StartWith("attachments/").And.EndWith(id.ToString("N"));
        row.BlobPath.Should().NotContain("screenshot");

        var blob = Container(factory).GetBlobClient(row.BlobPath);
        (await blob.ExistsAsync()).Value.Should().BeTrue();
        var properties = await blob.GetPropertiesAsync();
        properties.Value.ContentLength.Should().Be(Png.Length);
        properties.Value.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task Upload_Response_HasNoBlobPath()
    {
        var (client, _) = await NewUserAsync();

        var response = await UploadAsync(client, Png, "screenshot.png", "image/png");

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContainEquivalentOf("blob").And.NotContain("attachments/");
    }

    [Fact]
    public async Task Upload_FileNameWithTraversal_StoresOnlyTheName()
    {
        var (client, _) = await NewUserAsync();

        var response = await UploadAsync(client, Png, "..\\..\\etc\\shot.png", "image/png");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("fileName").GetString().Should().Be("shot.png");
    }

    [Fact]
    public async Task Upload_LogFile_Returns201()
    {
        var (client, _) = await NewUserAsync();

        var response = await UploadAsync(client, Encoding.UTF8.GetBytes("2026-10-13 ERROR boom"), "server.log", "text/plain");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Upload_26Mb_Returns413AndStoresNothing()
    {
        var (client, userId) = await NewUserAsync();
        var content = new byte[26 * 1024 * 1024];
        Array.Fill(content, (byte)'a');

        var response = await UploadAsync(client, content, "big.log", "text/plain");

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await CountForUploaderAsync(factory, userId)).Should().Be(0);
    }

    [Fact]
    public async Task Upload_JustOver25Mb_Returns413AndLeavesNoBlob()
    {
        var (client, userId) = await NewUserAsync();
        var content = new byte[(25 * 1024 * 1024) + 1];
        Array.Fill(content, (byte)'a');

        var response = await UploadAsync(client, content, "big.log", "text/plain");

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await CountForUploaderAsync(factory, userId)).Should().Be(0);
    }

    [Fact]
    public async Task Upload_Exactly25Mb_Returns201()
    {
        var (client, _) = await NewUserAsync();
        var content = new byte[25 * 1024 * 1024];
        Array.Fill(content, (byte)'a');

        var response = await UploadAsync(client, content, "max.log", "text/plain");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("setup.exe", "application/octet-stream")]
    [InlineData("logo.svg", "image/svg+xml")]
    [InlineData("shot.png", "text/plain")]
    public async Task Upload_DisallowedOrMismatchedType_Returns422OnFile(string fileName, string contentType)
    {
        var (client, userId) = await NewUserAsync();

        var response = await UploadAsync(client, Png, fileName, contentType);

        response.StatusCode.Should().Be((HttpStatusCode)422);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").TryGetProperty("file", out _).Should().BeTrue();
        (await CountForUploaderAsync(factory, userId)).Should().Be(0);
    }

    [Fact]
    public async Task Upload_Exe_Returns422()
    {
        var (client, _) = await NewUserAsync();

        var response = await UploadAsync(client, [0x4D, 0x5A, 0x90, 0x00], "setup.exe", "application/x-msdownload");

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Upload_PngThatIsReallyJpeg_Returns422AndLeavesNoBlob()
    {
        var (client, userId) = await NewUserAsync();
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0, 1];

        var response = await UploadAsync(client, jpeg, "shot.png", "image/png");

        response.StatusCode.Should().Be((HttpStatusCode)422);
        (await CountForUploaderAsync(factory, userId)).Should().Be(0);
    }

    [Fact]
    public async Task Upload_EmptyFile_Returns422()
    {
        var (client, _) = await NewUserAsync();

        var response = await UploadAsync(client, [], "empty.txt", "text/plain");

        response.StatusCode.Should().Be((HttpStatusCode)422);
    }

    [Fact]
    public async Task Upload_NoFilePart_Returns400()
    {
        var (client, _) = await NewUserAsync();
        var body = new MultipartFormDataContent { { new StringContent("hello"), "note" } };

        var response = await client.PostAsync("/api/v1/attachments", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_FilePartWithWrongName_Returns400()
    {
        var (client, _) = await NewUserAsync();

        var response = await client.PostAsync("/api/v1/attachments", FileBody(Png, "shot.png", "image/png", partName: "upload"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_NotMultipart_Returns400()
    {
        var (client, _) = await NewUserAsync();

        var response = await client.PostAsJsonAsync("/api/v1/attachments", new { file = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_TwoFileParts_Returns400AndStoresNothing()
    {
        var (client, userId) = await NewUserAsync();
        var first = new ByteArrayContent(Png);
        first.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        var second = new ByteArrayContent(Png);
        second.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        var body = new MultipartFormDataContent { { first, "file", "a.png" }, { second, "file", "b.png" } };

        var response = await client.PostAsync("/api/v1/attachments", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountForUploaderAsync(factory, userId)).Should().Be(0);
    }

    [Fact]
    public async Task Upload_NoToken_Returns401()
    {
        var anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await UploadAsync(anonymous, Png, "shot.png", "image/png");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
