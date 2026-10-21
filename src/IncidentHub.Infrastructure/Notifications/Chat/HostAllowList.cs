namespace IncidentHub.Infrastructure.Notifications.Chat;

/// <summary>Matches a host against exact entries and <c>*.suffix</c> patterns (whole labels only: <c>evilwebhook.office.com</c> is not <c>*.webhook.office.com</c>).</summary>
internal static class HostAllowList
{
    public static bool IsValidPattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var wildcard = pattern.StartsWith("*.", StringComparison.Ordinal);
        var host = wildcard ? pattern[2..] : pattern;

        // A wildcard over a bare top-level domain (*.com) would allow nearly everything.
        if (wildcard && !host.Contains('.'))
        {
            return false;
        }

        return !host.Contains('*') && Uri.CheckHostName(host) == UriHostNameType.Dns;
    }

    public static bool Allows(string host, IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = pattern[1..]; // ".webhook.office.com"
                if (host.Length > suffix.Length && host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
