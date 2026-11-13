namespace NeatShield.Core.Proxy;

/// <summary>
/// A proxy cannot see "a new window was opened". What it can see is a top-level navigation that comes from
/// another site. Those are the requests the popup rules of the filters ($popup, $popunder) are checked against.
/// A normal link to a site on a popup list in the same tab is caught too; those lists name ad networks,
/// so that is rare, and the block page says what happened.
/// </summary>
public static class PopupDetector
{
    /// <summary>
    /// True when the request is a page navigation that came from a different site. <paramref name="opener"/> is the page
    /// that started it (the Referer), or the URL itself when the browser did not send one.
    /// </summary>
    public static bool TryGetOpener(string url, string method, Func<string, string?> header, out string opener)
    {
        opener = url;
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)) return false;
        if (RequestClassifier.Classify(url, header) != "document") return false;

        var referer = header("Referer");
        var hasReferer = referer is not null &&
                         (referer.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          referer.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        var site = header("Sec-Fetch-Site")?.Trim().ToLowerInvariant();
        switch (site)
        {
            case "same-origin":
            case "same-site":
            case "none": // typed address, bookmark: the user's own choice
                return false;
            case "cross-site":
                break;
            default:
                // Older browsers: compare with the Referer.
                if (!hasReferer || SameSite(HostRules.HostOf(referer!), HostRules.HostOf(url))) return false;
                break;
        }

        if (hasReferer) opener = referer!;
        return true;
    }

    /// <summary>Rough "same site" test: same host, a subdomain of the other, or the same last two labels.</summary>
    internal static bool SameSite(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return true; // unknown: do not block
        if (a == b || a.EndsWith("." + b, StringComparison.Ordinal) || b.EndsWith("." + a, StringComparison.Ordinal)) return true;
        return LastTwoLabels(a) == LastTwoLabels(b);
    }

    private static string LastTwoLabels(string host)
    {
        var parts = host.Split('.');
        return parts.Length <= 2 ? host : parts[^2] + "." + parts[^1];
    }
}
