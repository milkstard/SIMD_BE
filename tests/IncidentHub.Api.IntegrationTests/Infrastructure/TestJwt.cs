using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace IncidentHub.Api.IntegrationTests.Infrastructure;

/// <summary>Issues JWTs signed with a throwaway RSA key so the pipeline's real validation runs without calling Entra.</summary>
public sealed class TestJwt
{
    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "test-key" };

    public SecurityKey SigningKey => _key;

    public string Issuer(string tenantId) => $"https://login.microsoftonline.com/{tenantId}/v2.0";

    public string Create(
        string tenantId,
        string audience,
        string objectId,
        IEnumerable<string> groups,
        string scope = "access_as_user",
        DateTime? expires = null,
        string? tokenTenantId = null)
    {
        var expiry = expires ?? DateTime.UtcNow.AddMinutes(30);
        var issued = expiry.AddHours(-2);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer(tenantId),
            Audience = audience,
            NotBefore = issued,
            IssuedAt = issued,
            Expires = expiry,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["oid"] = objectId,
                ["tid"] = tokenTenantId ?? tenantId,
                ["name"] = "Jwt User",
                ["preferred_username"] = $"{objectId}@test.local",
                ["scp"] = scope,
                ["groups"] = groups.ToArray(),
            },
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }
}
