using System.Text.Json.Nodes;
using FluentAssertions;
using IncidentHub.Application.Dashboard;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Writers;
using Swashbuckle.AspNetCore.Swagger;

namespace IncidentHub.Api.UnitTests.OpenApi;

/// <summary>Hosts the API with the same fixed placeholder settings as <c>scripts/generate-openapi.ps1</c>; no Docker, no hosted services.</summary>
public sealed class OpenApiHostFixture : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Sql", "Server=localhost;Database=openapi;Trusted_Connection=true");
        builder.UseSetting("ConnectionStrings:Redis", "localhost:6379");
        builder.UseSetting("Storage:Blob:ConnectionString", "UseDevelopmentStorage=true");
        builder.UseSetting("AzureAd:TenantId", "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAd:ClientId", "00000000-0000-0000-0000-000000000002");
        builder.UseSetting("Authorization:GroupRoleMap:00000000-0000-0000-0000-000000000003", "Admin");
        builder.ConfigureTestServices(services =>
        {
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
            {
                services.Remove(hosted);
            }
        });
    }

    public string Generate()
    {
        var swagger = Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        using var writer = new StringWriter();
        swagger.SerializeAsV3(new OpenApiJsonWriter(writer));
        return writer.ToString();
    }
}

public sealed class OpenApiDocumentTests(OpenApiHostFixture host) : IClassFixture<OpenApiHostFixture>
{
    private JsonNode Document() => JsonNode.Parse(host.Generate())!;

    private static string Normalize(string json) => json.Replace("\r\n", "\n").TrimEnd();

    [Fact]
    public void Generate_TwiceFromSameHost_IsIdentical()
    {
        host.Generate().Should().Be(host.Generate());
    }

    [Fact]
    public void Generate_ContainsBe11Endpoints()
    {
        var doc = Document();
        var paths = doc["paths"]!.AsObject();

        paths.ContainsKey("/api/v1/dashboard/app-status").Should().BeTrue();
        paths.ContainsKey("/api/v1/dashboard/summary").Should().BeTrue();
        paths.ContainsKey("/api/v1/me").Should().BeTrue();

        var range = paths["/api/v1/dashboard/summary"]!["get"]!["parameters"]!.AsArray()
            .Single(p => p!["name"]!.GetValue<string>() == "range")!["schema"]!;
        range["enum"]!.AsArray().Select(v => v!.GetValue<string>())
            .Should().Equal(Enum.GetValues<DashboardRange>().Select(r => r.Token()).OrderBy(t => t == "24h" ? 0 : t == "7d" ? 1 : 2));
        range["default"]!.GetValue<string>().Should().Be("7d");

        var schemas = doc["components"]!["schemas"]!.AsObject();
        schemas["AppStatus"]!["type"]!.GetValue<string>().Should().Be("string");
        schemas["CurrentUserDto"]!["properties"]!["roles"]!["items"]!["$ref"]!.GetValue<string>().Should().EndWith("/UserRole");
        schemas["UserRole"]!["type"]!.GetValue<string>().Should().Be("string");
    }

    [Fact]
    public void Generate_PathsAndSchemasAreSorted()
    {
        var doc = Document();

        var paths = doc["paths"]!.AsObject().Select(p => p.Key).ToList();
        paths.Should().Equal(paths.OrderBy(p => p, StringComparer.Ordinal));
        var schemas = doc["components"]!["schemas"]!.AsObject().Select(p => p.Key).ToList();
        schemas.Should().Equal(schemas.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void Generate_NoRequestSchemaContainsAppStatus()
    {
        var doc = Document();
        var schemas = doc["components"]!["schemas"]!.AsObject();
        var roots = new List<JsonNode>();
        foreach (var (_, item) in doc["paths"]!.AsObject())
        {
            foreach (var (_, operation) in item!.AsObject())
            {
                foreach (var content in operation!["requestBody"]?["content"]?.AsObject().Select(c => c.Value) ?? [])
                {
                    roots.Add(content!["schema"]!);
                }

                foreach (var parameter in operation["parameters"]?.AsArray() ?? [])
                {
                    roots.Add(parameter!["schema"]!);
                }
            }
        }

        var seen = new HashSet<string>();
        var queue = new Queue<JsonNode>(roots);
        while (queue.Count > 0)
        {
            Walk(queue.Dequeue(), schemas, seen, queue);
        }

        seen.Should().NotContain("AppStatus");
        seen.Should().NotContain("AppStatusItemDto");
    }

    private static void Walk(JsonNode node, JsonObject schemas, HashSet<string> seen, Queue<JsonNode> queue)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (key == "$ref" && value!.GetValue<string>() is { } reference)
                    {
                        var name = reference[(reference.LastIndexOf('/') + 1)..];
                        if (seen.Add(name))
                        {
                            queue.Enqueue(schemas[name]!);
                        }
                    }
                    else if (value is not null)
                    {
                        Walk(value, schemas, seen, queue);
                    }
                }

                break;
            case JsonArray array:
                foreach (var child in array.Where(c => c is not null))
                {
                    Walk(child!, schemas, seen, queue);
                }

                break;
        }
    }

    [Fact]
    public void Generate_MatchesCommittedSwagger()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IncidentHub.sln")))
        {
            directory = directory.Parent;
        }

        var committed = directory is null ? null : Path.Combine(directory.FullName, "contracts", "swagger.json");
        if (committed is null || !File.Exists(committed))
        {
            return; // fresh clone without the generated contract
        }

        Normalize(host.Generate()).Should().Be(Normalize(File.ReadAllText(committed)));
    }
}
