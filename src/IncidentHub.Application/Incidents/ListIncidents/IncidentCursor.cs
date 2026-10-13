using System.Text;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;

namespace IncidentHub.Application.Incidents.ListIncidents;

/// <summary>Opaque keyset position of an incident list page, bound to the sort it was issued for.</summary>
/// <param name="Time">The sort time (<c>CreatedAt</c> or <c>UpdatedAt</c>); for the severity sort the tie-break <c>CreatedAt</c>.</param>
/// <param name="Rank">Severity rank, only meaningful for the severity sort.</param>
public sealed record IncidentCursor(IncidentSort Sort, DateTimeOffset Time, int Rank, Guid Id)
{
    public string Encode() =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>True when the cursor is absent (<paramref name="result"/> null) or valid for <paramref name="sort"/>.</summary>
    public static bool TryDecode(string? cursor, IncidentSort sort, out IncidentCursor? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return true;
        }

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var decoded = JsonSerializer.Deserialize<IncidentCursor>(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
            if (decoded is null || decoded.Sort != sort || decoded.Id == Guid.Empty)
            {
                return false;
            }

            result = decoded;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Decodes a client-supplied cursor or fails validation on <c>cursor</c>; null when there is none.</summary>
    public static IncidentCursor? DecodeOrThrow(string? cursor, IncidentSort sort) =>
        TryDecode(cursor, sort, out var result)
            ? result
            : throw new ValidationException([new ValidationFailure("cursor", "Cursor is invalid.")]);
}
