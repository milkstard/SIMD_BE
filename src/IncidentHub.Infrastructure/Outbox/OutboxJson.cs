using System.Text.Json;
using System.Text.Json.Serialization;

namespace IncidentHub.Infrastructure.Outbox;

/// <summary>Serializer settings for outbox payloads; the BE-10 dispatcher reads them back with the same options.</summary>
public static class OutboxJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
