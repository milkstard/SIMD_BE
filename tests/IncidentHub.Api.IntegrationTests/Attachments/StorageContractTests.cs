using System.Net;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FluentAssertions;
using IncidentHub.Api.IntegrationTests.Infrastructure;
using IncidentHub.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using static IncidentHub.Api.IntegrationTests.Attachments.AttachmentTestSupport;

namespace IncidentHub.Api.IntegrationTests.Attachments;

/// <summary><see cref="AzureBlobFileStore"/> against the Azurite container.</summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class StorageContractTests(ApiFactory factory)
{
    private async Task<AzureBlobFileStore> NewStoreAsync()
    {
        var service = new BlobServiceClient(factory.BlobConnectionString);
        var container = $"contract-{Guid.NewGuid():N}";
        await service.GetBlobContainerClient(container).CreateIfNotExistsAsync();
        return new AzureBlobFileStore(service, Options.Create(new BlobStorageOptions { Container = container }), TimeProvider.System);
    }

    [Fact]
    public async Task Container_ExistsAndIsPrivate_AfterHostStart()
    {
        var container = Container(factory);

        (await container.ExistsAsync()).Value.Should().BeTrue();
        var access = await container.GetAccessPolicyAsync();
        access.Value.BlobPublicAccess.Should().Be(PublicAccessType.None);
    }

    [Fact]
    public async Task Store_UploadReadDelete_RoundTrips()
    {
        var store = await NewStoreAsync();
        const string path = "attachments/2026/10/roundtrip";

        await store.UploadAsync(path, new MemoryStream(Png), "image/png", CancellationToken.None);
        await using (var read = await store.OpenReadAsync(path, CancellationToken.None))
        {
            using var copy = new MemoryStream();
            await read.CopyToAsync(copy);
            copy.ToArray().Should().Equal(Png);
        }

        await store.DeleteAsync(path, CancellationToken.None);
        var reread = () => store.OpenReadAsync(path, CancellationToken.None);
        (await reread.Should().ThrowAsync<RequestFailedException>()).Which.Status.Should().Be(404);
    }

    [Fact]
    public async Task Store_DeleteMissingBlob_DoesNotThrow()
    {
        var store = await NewStoreAsync();

        var act = () => store.DeleteAsync("attachments/missing", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Store_UploadNonSeekableStream_StoresAllBytes()
    {
        var store = await NewStoreAsync();
        var content = new byte[9 * 1024 * 1024];
        new Random(7).NextBytes(content);

        await store.UploadAsync("attachments/big", new NonSeekableStream(content), "text/plain", CancellationToken.None);

        await using var read = await store.OpenReadAsync("attachments/big", CancellationToken.None);
        using var copy = new MemoryStream();
        await read.CopyToAsync(copy);
        copy.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task Store_Sas_ForcesAttachmentDispositionAndEncodesTheName()
    {
        var store = await NewStoreAsync();
        await store.UploadAsync("attachments/x", new MemoryStream(Png), "image/png", CancellationToken.None);

        var uri = await store.CreateDownloadUriAsync("attachments/x", "résumé \"x\".png", "image/png", TimeSpan.FromMinutes(1), CancellationToken.None);
        using var http = new HttpClient();
        var response = await http.GetAsync(uri);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var disposition = string.Join(' ', response.Content.Headers.GetValues("Content-Disposition"));
        disposition.Should().StartWith("attachment;");
        disposition.Should().Contain("filename=\"r_sum_ _x_.png\"");
        disposition.Should().Contain("filename*=UTF-8''r%C3%A9sum%C3%A9%20%22x%22.png");
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
    }

    [Fact]
    public async Task Store_Sas_ForNonImageUsesOctetStream()
    {
        var store = await NewStoreAsync();
        await store.UploadAsync("attachments/t", new MemoryStream("<b>x</b>"u8.ToArray()), "text/plain", CancellationToken.None);

        var uri = await store.CreateDownloadUriAsync("attachments/t", "t.log", "text/plain", TimeSpan.FromMinutes(1), CancellationToken.None);
        using var http = new HttpClient();
        var response = await http.GetAsync(uri);

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task Store_Sas_ExpiresAtTheRequestedLifetime()
    {
        var store = await NewStoreAsync();
        await store.UploadAsync("attachments/e", new MemoryStream(Png), "image/png", CancellationToken.None);
        var before = DateTimeOffset.UtcNow;

        var uri = await store.CreateDownloadUriAsync("attachments/e", "e.png", "image/png", TimeSpan.FromMinutes(5), CancellationToken.None);

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var expires = DateTimeOffset.Parse(query["se"]!, null, System.Globalization.DateTimeStyles.AssumeUniversal);
        expires.Should().BeCloseTo(before.AddMinutes(5), TimeSpan.FromSeconds(5));
        query["sp"].Should().Be("r");
    }

    /// <summary>Like a request body: no length, no seeking.</summary>
    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(count, data.Length - _position);
            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
