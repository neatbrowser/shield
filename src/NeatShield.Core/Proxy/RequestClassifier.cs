namespace NeatShield.Core.Proxy;

/// <summary>
/// Works out what kind of request a browser sent, using the headers it adds itself
/// (Sec-Fetch-Dest / Sec-Fetch-Mode). Older clients get a guess from the file extension and Accept header.
/// Returns one of the request types neat_check_request understands.
/// </summary>
public static class RequestClassifier
{
    /// <param name="header">Returns the value of a request header, or null when it is absent.</param>
    public static string Classify(string url, Func<string, string?> header)
    {
        if (string.Equals(header("Upgrade"), "websocket", StringComparison.OrdinalIgnoreCase))
        {
            return "websocket";
        }

        var dest = header("Sec-Fetch-Dest")?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(dest))
        {
            switch (dest)
            {
                case "document": return "document";
                case "iframe":
                case "frame":
                case "fencedframe": return "subdocument";
                case "style": return "stylesheet";
                case "script":
                case "worker":
                case "sharedworker":
                case "serviceworker":
                case "audioworklet":
                case "paintworklet": return "script";
                case "image": return "image";
                case "audio":
                case "video":
                case "track": return "media";
                case "font": return "font";
                case "object":
                case "embed": return "object";
                case "report": return "ping";
                case "empty":
                    // fetch() / XHR / EventSource. A navigation never has dest=empty.
                    return "xmlhttprequest";
                default: return "other";
            }
        }

        if (string.Equals(header("Sec-Fetch-Mode")?.Trim(), "navigate", StringComparison.OrdinalIgnoreCase))
        {
            return "document";
        }

        if (string.Equals(header("X-Requested-With"), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
        {
            return "xmlhttprequest";
        }

        var byExt = FromExtension(url);
        if (byExt is not null)
        {
            return byExt;
        }

        var accept = header("Accept")?.ToLowerInvariant() ?? "";
        if (accept.StartsWith("text/html", StringComparison.Ordinal)) return "document";
        if (accept.StartsWith("text/css", StringComparison.Ordinal)) return "stylesheet";
        if (accept.StartsWith("image/", StringComparison.Ordinal)) return "image";
        return "other";
    }

    /// <summary>
    /// The page that made the request. Referer first, then Origin; for a page itself (no referer) the URL is its own source.
    /// </summary>
    public static string SourceUrl(string url, string type, Func<string, string?> header)
    {
        var referer = header("Referer");
        if (IsHttpUrl(referer)) return referer!;

        var origin = header("Origin");
        if (IsHttpUrl(origin)) return origin!;

        return url;
    }

    private static string? FromExtension(string url)
    {
        var end = url.IndexOfAny(['?', '#']);
        var path = end >= 0 ? url[..end] : url;
        var dot = path.LastIndexOf('.');
        var slash = path.LastIndexOf('/');
        if (dot < 0 || dot < slash) return null;

        return path[(dot + 1)..].ToLowerInvariant() switch
        {
            "js" or "mjs" => "script",
            "css" => "stylesheet",
            "png" or "jpg" or "jpeg" or "gif" or "webp" or "avif" or "svg" or "ico" or "bmp" => "image",
            "mp4" or "webm" or "mp3" or "ogg" or "m4a" or "m3u8" or "ts" or "wav" => "media",
            "woff" or "woff2" or "ttf" or "otf" or "eot" => "font",
            _ => null,
        };
    }

    private static bool IsHttpUrl(string? s) =>
        s is not null && (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          s.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}
