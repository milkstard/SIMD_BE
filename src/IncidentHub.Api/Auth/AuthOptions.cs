using IncidentHub.Domain.Users;
using Microsoft.Extensions.Options;

namespace IncidentHub.Api.Auth;

public sealed class AzureAdSettings
{
    public const string SectionName = "AzureAd";

    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    public string TenantId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;
}

public sealed class GroupRoleOptions
{
    public const string SectionName = "Authorization";

    /// <summary>Entra group object id → application role.</summary>
    public Dictionary<string, UserRole> GroupRoleMap { get; set; } = [];
}

public sealed class AzureAdSettingsValidator : IValidateOptions<AzureAdSettings>
{
    private static readonly string[] MultiTenantAliases = ["common", "organizations", "consumers"];

    public ValidateOptionsResult Validate(string? name, AzureAdSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("AzureAd:ClientId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.TenantId))
        {
            failures.Add("AzureAd:TenantId is required.");
        }
        else if (MultiTenantAliases.Contains(options.TenantId, StringComparer.OrdinalIgnoreCase))
        {
            failures.Add("AzureAd:TenantId must be a specific tenant; multi-tenant aliases are not supported.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class GroupRoleOptionsValidator : IValidateOptions<GroupRoleOptions>
{
    public ValidateOptionsResult Validate(string? name, GroupRoleOptions options)
    {
        if (options.GroupRoleMap.Count == 0)
        {
            return ValidateOptionsResult.Fail("Authorization:GroupRoleMap must map at least one Entra group to a role.");
        }

        var badKeys = options.GroupRoleMap.Keys.Where(k => !Guid.TryParse(k, out _)).ToList();
        return badKeys.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Authorization:GroupRoleMap keys must be group object ids (GUIDs): {string.Join(", ", badKeys)}");
    }
}
