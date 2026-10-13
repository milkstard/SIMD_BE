using System.Text;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;

namespace IncidentHub.Application.Common.Paging;

/// <summary>Opaque keyset cursor over a <c>(CreatedAt, Id)</c> ordering, encoded as base64url JSON.</summary>
public static class TimeKeysetCursor
{
    private sealed record Payload(DateTimeOffset At, Guid Id);

    public static string Encode(DateTimeOffset at, Guid id) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Payload(at, id)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Decodes a client-supplied cursor or fails validation on <c>cursor</c>; null when there is none.</summary>
    public static (DateTimeOffset At, Guid Id)? DecodeOrThrow(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var payload = JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
            if (payload is not null && payload.Id != Guid.Empty)
            {
                return (payload.At, payload.Id);
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            // fall through to the validation error
        }

        throw new ValidationException([new ValidationFailure("cursor", "Cursor is invalid.")]);
    }
}
