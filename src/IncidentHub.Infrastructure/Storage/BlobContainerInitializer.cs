using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IncidentHub.Infrastructure.Storage;

/// <summary>
/// Creates the private attachments container once when a host starts (API and Worker), so requests never create it.
/// Idempotent: an existing container is left as it is.
/// </summary>
public sealed class BlobContainerInitializer(
    BlobServiceClient service,
    IOptions<BlobStorageOptions> options,
    ILogger<BlobContainerInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var container = service.GetBlobContainerClient(options.Value.Container);
            var created = await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

            if (created is not null)
            {
                logger.LogInformation("Blob container {Container} created", options.Value.Container);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Storage being down must not stop the whole host (incidents, comments and the hub do not need it);
            // uploads fail until the container exists, and the next restart tries again.
            logger.LogError(exception, "Blob container {Container} could not be created", options.Value.Container);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
