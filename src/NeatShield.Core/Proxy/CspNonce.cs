using System.Security.Cryptography;

namespace NeatShield.Core.Proxy;

/// <summary>
/// Pages with a Content-Security-Policy refuse inline &lt;style&gt; and &lt;script&gt; unless they carry a nonce
/// the policy lists. We add our own random nonce to the policy and to the tags we inject.
/// </summary>
public static class CspNonce
{
    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    private static readonly string[] Targets = ["script-src", "script-src-elem", "style-src", "style-src-elem", "default-src"];

    /// <summary>Returns the policy with the nonce added where inline code would otherwise be blocked.</summary>
    public static string AddNonce(string policy, string nonce)
    {
        var token = $"'nonce-{nonce}'";
        var parts = policy.Split(';');
        for (var i = 0; i < parts.Length; i++)
        {
            var directive = parts[i].Trim();
            if (directive.Length == 0) continue;

            var space = directive.IndexOfAny([' ', '\t']);
            var name = (space < 0 ? directive : directive[..space]).ToLowerInvariant();
            if (Array.IndexOf(Targets, name) < 0) continue;

            var values = space < 0 ? "" : directive[(space + 1)..].Trim();
            var list = values.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList();

            // Only 'unsafe-inline' on its own already allows our inline code. Any nonce or hash switches it off.
            var hasUnsafeInline = list.Contains("'unsafe-inline'", StringComparer.OrdinalIgnoreCase);
            var hasNonceOrHash = list.Any(v =>
                v.StartsWith("'nonce-", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith("'sha256-", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith("'sha384-", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith("'sha512-", StringComparison.OrdinalIgnoreCase));
            if (hasUnsafeInline && !hasNonceOrHash) continue;

            list.RemoveAll(v => v.Equals("'none'", StringComparison.OrdinalIgnoreCase));
            list.Add(token);
            parts[i] = " " + name + " " + string.Join(' ', list);
        }

        return string.Join(';', parts).Trim();
    }
}
