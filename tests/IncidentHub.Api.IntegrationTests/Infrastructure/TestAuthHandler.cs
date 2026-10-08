using System.Security.Claims;
using System.Text.Encodings.Web;
using IncidentHub.Api.Auth;
using IncidentHub.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IncidentHub.Api.IntegrationTests.Infrastructure;

/// <summary>Header-driven authentication: <c>X-Test-Oid</c>, <c>X-Test-Groups</c> (comma separated), <c>X-Test-Scope</c>.</summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiFactory factory) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string OidHeader = "X-Test-Oid";
    public const string GroupsHeader = "X-Test-Groups";
    public const string ScopeHeader = "X-Test-Scope";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(OidHeader, out var oid))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(AuthClaims.ObjectId, oid.ToString()),
            new(AuthClaims.TenantId, factory.TenantId),
            new(AuthClaims.Name, "Test User"),
            new("preferred_username", $"{oid}@test.local"),
            new(AuthClaims.Scope, Request.Headers.TryGetValue(ScopeHeader, out var scope) ? scope.ToString() : AuthClaims.RequiredScope),
        };

        if (Request.Headers.TryGetValue(GroupsHeader, out var groups))
        {
            claims.AddRange(groups.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(g => new Claim(AuthClaims.Groups, g)));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public static class TestClientExtensions
{
    /// <summary>Authenticates the client as <paramref name="objectId"/> holding the groups that map to <paramref name="roles"/>.</summary>
    public static HttpClient AsUser(
        this HttpClient client,
        ApiFactory factory,
        string objectId,
        UserRole[] roles,
        string? scope = null)
    {
        client.DefaultRequestHeaders.Add(TestAuthHandler.OidHeader, objectId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.GroupsHeader, string.Join(',', roles.Select(factory.GroupFor)));
        if (scope is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        }

        return client;
    }

    public static HttpClient AsUser(this HttpClient client, ApiFactory factory, string objectId, params UserRole[] roles) =>
        client.AsUser(factory, objectId, roles, scope: null);
}
