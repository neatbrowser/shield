using System.Net;

namespace NeatShield.Core.Proxy;

/// <summary>Decides which HTTPS hosts are opened up (decrypted) by the proxy and which are passed through untouched.</summary>
public static class HostRules
{
    public static bool ShouldDecrypt(string? host, IReadOnlyCollection<string> userExcluded)
    {
        host = Normalize(host);
        if (host.Length == 0) return false;

        // Raw IP addresses and local names: no certificate to forge, nothing to filter.
        if (IPAddress.TryParse(host, out _)) return false;
        if (!host.Contains('.')) return false;
        if (host is "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal) ||
            host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".internal", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var pattern in userExcluded)
        {
            if (Matches(host, pattern)) return false;
        }

        return true;
    }

    /// <summary>True when the host (or a parent domain) is in the user's exclusion list.</summary>
    public static bool IsExcluded(string host, IReadOnlyCollection<string> userExcluded)
    {
        host = Normalize(host);
        return host.Length > 0 && userExcluded.Any(p => Matches(host, p));
    }

    /// <summary>"example.com" matches example.com and every subdomain; "*.example.com" does too.</summary>
    public static bool Matches(string host, string pattern)
    {
        pattern = Normalize(pattern);
        if (pattern.StartsWith("*.", StringComparison.Ordinal)) pattern = pattern[2..];
        if (pattern.Length == 0) return false;
        return host.Equals(pattern, StringComparison.Ordinal) || host.EndsWith("." + pattern, StringComparison.Ordinal);
    }

    public static string Normalize(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return "";
        host = host.Trim().ToLowerInvariant().TrimEnd('.');
        // Strip a port: "example.com:443". IPv6 literals ("[::1]:443") keep their brackets removed.
        if (host.StartsWith('['))
        {
            var close = host.IndexOf(']');
            return close > 0 ? host[1..close] : host;
        }

        var colon = host.LastIndexOf(':');
        if (colon > 0 && host.IndexOf(':') == colon) host = host[..colon];
        return host;
    }

    /// <summary>Host name of a URL, lower case, without port. Empty when the URL is not valid.</summary>
    public static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.IdnHost.ToLowerInvariant() : "";
}
