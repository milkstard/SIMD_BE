using System.Security.Cryptography;
using System.Text;

namespace IncidentHub.Domain.Incidents;

/// <summary>Dedup hash of application + normalized title; two open incidents may not share one.</summary>
public static class IncidentFingerprint
{
    public static string Compute(Guid applicationId, string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var normalized = Normalize(title);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{applicationId:N}|{normalized}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Normalize(string title)
    {
        var builder = new StringBuilder(title.Length);
        var pendingSpace = false;
        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                builder.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }
}
