using Microsoft.Extensions.Options;

namespace IncidentHub.Infrastructure.Storage;

/// <summary>
/// <c>Storage:Blob</c>. Use <see cref="ConnectionString"/> locally (Azurite, from user-secrets) or <see cref="AccountUri"/>
/// in Azure, where access goes through the managed identity.
/// </summary>
public sealed class BlobStorageOptions
{
    public const string SectionName = "Storage:Blob";

    public string? ConnectionString { get; set; }

    public string? AccountUri { get; set; }

    public string Container { get; set; } = "attachments";
}

public sealed class BlobStorageOptionsValidator : IValidateOptions<BlobStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, BlobStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString) && string.IsNullOrWhiteSpace(options.AccountUri))
        {
            return ValidateOptionsResult.Fail(
                "Storage:Blob:ConnectionString or Storage:Blob:AccountUri is not configured. Set it with user-secrets or environment variables.");
        }

        if (!string.IsNullOrWhiteSpace(options.AccountUri) && !Uri.TryCreate(options.AccountUri, UriKind.Absolute, out _))
        {
            return ValidateOptionsResult.Fail("Storage:Blob:AccountUri must be an absolute URI.");
        }

        return string.IsNullOrWhiteSpace(options.Container)
            ? ValidateOptionsResult.Fail("Storage:Blob:Container must not be empty.")
            : ValidateOptionsResult.Success;
    }
}
