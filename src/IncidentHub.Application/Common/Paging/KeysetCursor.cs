using System.Text;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;

namespace IncidentHub.Application.Common.Paging;

/// <summary>Opaque keyset cursor over a <c>(Name, Id)</c> ordering, encoded as base64url JSON.</summary>
public static class KeysetCursor
{
    private sealed record Payload(string Name, Guid Id);

    public static string Encode(string name, Guid id) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Payload(name, id)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static bool TryDecode(string? cursor, out string name, out Guid id)
    {
        name = string.Empty;
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var payload = JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
            if (payload?.Name is null)
            {
                return false;
            }

            name = payload.Name;
            id = payload.Id;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Decodes a client-supplied cursor or fails validation on <c>cursor</c>.</summary>
    public static (string Name, Guid Id)? DecodeOrThrow(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        return TryDecode(cursor, out var name, out var id)
            ? (name, id)
            : throw new ValidationException([new ValidationFailure("cursor", "Cursor is invalid.")]);
    }
}
