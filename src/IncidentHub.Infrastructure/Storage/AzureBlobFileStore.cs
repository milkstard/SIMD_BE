using System.Text;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Incidents;
using IncidentHub.Infrastructure.Telemetry;
using Microsoft.Extensions.Options;

namespace IncidentHub.Infrastructure.Storage;

/// <summary>
/// <see cref="IFileStore"/> on Azure Blob Storage (Azurite locally). Downloads are never streamed through the API: the caller is
/// redirected to a read-only SAS link that expires after the requested lifetime. Retries are the Azure SDK's own policy
/// (<see cref="BlobClientOptions.Retry"/>); a Polly retry cannot replay a non-seekable upload stream.
/// </summary>
public sealed class AzureBlobFileStore(
    BlobServiceClient service,
    IOptions<BlobStorageOptions> options,
    TimeProvider time) : IFileStore
{
    // Small blocks keep memory flat: the SDK buffers one block of a non-seekable stream, not the whole file.
    private const int BlockSize = 4 * 1024 * 1024;

    private static readonly TimeSpan DelegationKeyLifetime = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _delegationKeyLock = new(1, 1);
    private UserDelegationKey? _delegationKey;

    private BlobContainerClient Container => service.GetBlobContainerClient(options.Value.Container);

    public async Task UploadAsync(string path, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("blob.upload");
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            TransferOptions = new StorageTransferOptions
            {
                InitialTransferSize = BlockSize,
                MaximumTransferSize = BlockSize,
            },
        };

        await Container.GetBlobClient(path).UploadAsync(content, uploadOptions, cancellationToken);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("blob.delete");
        await Container.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task<Uri> CreateDownloadUriAsync(
        string path,
        string fileName,
        string contentType,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("blob.sas");
        var blob = Container.GetBlobClient(path);
        var now = time.GetUtcNow();

        var sas = new BlobSasBuilder
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",
            ExpiresOn = now + lifetime,
            ContentDisposition = ContentDisposition(fileName),

            // Anything that is not an image is served as opaque bytes, so a browser never renders it.
            ContentType = AttachmentRules.IsImage(contentType) ? contentType : "application/octet-stream",
        };
        sas.SetPermissions(BlobSasPermissions.Read);

        // HTTPS only, except for the local emulator, which serves plain HTTP.
        if (blob.Uri.Scheme == Uri.UriSchemeHttps)
        {
            sas.Protocol = SasProtocol.Https;
        }

        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(sas);
        }

        var key = await GetDelegationKeyAsync(now, cancellationToken);
        return new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(key, service.AccountName) }.ToUri();
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken) =>
        await Container.GetBlobClient(path).OpenReadAsync(cancellationToken: cancellationToken);

    /// <summary>
    /// <c>attachment</c> with an ASCII fallback <c>filename</c> and the exact name as an RFC 5987 <c>filename*</c>;
    /// the SAS header cannot carry non-ASCII characters.
    /// </summary>
    internal static string ContentDisposition(string fileName)
    {
        var fallback = new string(fileName.Select(c => c is >= ' ' and <= '~' and not '"' and not '\\' and not '%' and not ';' ? c : '_').ToArray());
        return $"attachment; filename=\"{fallback}\"; filename*=UTF-8''{Rfc5987Encode(fileName)}";
    }

    private static string Rfc5987Encode(string value)
    {
        var builder = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "!#$&+-.^_`|~".Contains(c)))
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2"));
            }
        }

        return builder.ToString();
    }

    private async Task<UserDelegationKey> GetDelegationKeyAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Reuse the key until a link issued from it could outlive it.
        if (_delegationKey is { } cached && cached.SignedExpiresOn > now.AddMinutes(30))
        {
            return cached;
        }

        await _delegationKeyLock.WaitAsync(cancellationToken);
        try
        {
            if (_delegationKey is not { } current || current.SignedExpiresOn <= now.AddMinutes(30))
            {
                Response<UserDelegationKey> response = await service.GetUserDelegationKeyAsync(
                    now.AddMinutes(-5),
                    now + DelegationKeyLifetime,
                    cancellationToken);
                _delegationKey = response.Value;
            }

            return _delegationKey;
        }
        finally
        {
            _delegationKeyLock.Release();
        }
    }
}
