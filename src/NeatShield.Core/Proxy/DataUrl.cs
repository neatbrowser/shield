using System.Net;
using System.Text;

namespace NeatShield.Core.Proxy;

/// <summary>Reads the data: URL the engine gives for a redirect rule (a harmless stand-in resource).</summary>
public static class DataUrl
{
    public readonly record struct Parsed(string ContentType, byte[] Body);

    public static bool TryParse(string? url, out Parsed result)
    {
        result = default;
        if (url is null || !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;

        var comma = url.IndexOf(',');
        if (comma < 0) return false;

        var meta = url[5..comma];
        var data = url[(comma + 1)..];
        var base64 = false;
        var type = "text/plain";

        var segments = meta.Split(';');
        if (segments[0].Length > 0) type = segments[0];
        foreach (var seg in segments.Skip(1))
        {
            if (seg.Equals("base64", StringComparison.OrdinalIgnoreCase)) base64 = true;
        }

        try
        {
            var body = base64
                ? Convert.FromBase64String(WebUtility.UrlDecode(data))
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data));
            result = new Parsed(type, body);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
