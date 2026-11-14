using System.Text;
using System.Text.RegularExpressions;

namespace NeatShield.Core.Proxy;

/// <summary>Builds the hide-CSS and the scripts of a page and puts them in its HTML.</summary>
public static partial class HtmlInjector
{
    [GeneratedRegex(@"<head(?=[\s>/])[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadOpen();

    [GeneratedRegex(@"<html(?=[\s>/])[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlOpen();

    [GeneratedRegex(@"^\s*(<!--.*?-->\s*)*<!doctype[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Doctype();

    [GeneratedRegex(@"<meta(?=[\s/>])[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaTag();

    [GeneratedRegex("""([^\s"'<>/=]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+)))?""", RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();

    private const int MaxSelectors = 20000;

    /// <summary>A policy in a &lt;meta&gt; tag has to be in the head, so only the start of the page is searched.</summary>
    private const int MetaSearchLimit = 128 * 1024;

    /// <summary>Hide rule for a list of selectors. Selectors with a brace or a closing tag are dropped (they cannot be one valid selector).</summary>
    public static string BuildHideCss(IEnumerable<string> selectors)
    {
        var sb = new StringBuilder();
        var count = 0;
        foreach (var s in selectors)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Contains('{') || s.Contains('}') || s.Contains("</", StringComparison.Ordinal)) continue;
            // One rule per selector: a selector the browser cannot parse must not break the others.
            sb.Append(s.Trim()).Append("{display:none!important}\n");
            if (++count >= MaxSelectors) break;
        }

        return sb.ToString();
    }

    /// <summary>The style and script tags for a page, or "" when there is nothing to add.</summary>
    public static string BuildBlock(string css, string script, string nonce) =>
        BuildBlock(css, script.Length > 0 ? new[] { script } : Array.Empty<string>(), nonce);

    /// <summary>Each script gets its own tag, so one that fails does not stop the others.</summary>
    public static string BuildBlock(string css, IReadOnlyList<string> scripts, string nonce)
    {
        var block = new StringBuilder();
        if (css.Length > 0)
        {
            block.Append("<style nonce=\"").Append(nonce).Append("\">")
                .Append(css.Replace("</style", "<\\/style", StringComparison.OrdinalIgnoreCase))
                .Append("</style>");
        }

        foreach (var script in scripts)
        {
            if (script.Length == 0) continue;
            block.Append("<script nonce=\"").Append(nonce).Append("\">")
                .Append(script.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase))
                .Append("</script>");
        }

        return block.ToString();
    }

    /// <summary>Where the block goes: right after &lt;head&gt;, else after &lt;html&gt;, else after the doctype, else at the start.</summary>
    internal static int FindInsertIndex(string html)
    {
        var m = HeadOpen().Match(html);
        if (!m.Success) m = HtmlOpen().Match(html);
        if (!m.Success) m = Doctype().Match(html);
        return m.Success ? m.Index + m.Length : 0;
    }

    /// <summary>Inserts the CSS and script as early as possible. Returns the html unchanged when there is nothing to add.</summary>
    public static string Inject(string html, string css, string script, string nonce)
    {
        var block = BuildBlock(css, script, nonce);
        return block.Length == 0 ? html : html.Insert(FindInsertIndex(html), block);
    }

    /// <summary>
    /// Passes the policy of every <c>&lt;meta http-equiv="Content-Security-Policy" content="..."&gt;</c> at the start of the page
    /// through <paramref name="patch"/> (a page can set its policy in the HTML instead of in a header). Returns the same
    /// string when there is no such tag.
    /// </summary>
    public static string PatchMetaCsp(string html, Func<string, string> patch)
    {
        var limit = Math.Min(html.Length, MetaSearchLimit);
        var matches = MetaTag().Matches(html, 0).Where(m => m.Index < limit).ToList();
        if (matches.Count == 0) return html;

        StringBuilder? sb = null;
        var copied = 0;
        foreach (var tag in matches)
        {
            string? httpEquiv = null;
            Group? content = null;
            string? contentQuote = null;
            foreach (Match a in Attribute().Matches(tag.Value.Substring(5)))
            {
                var name = a.Groups[1].Value;
                if (name.Equals("http-equiv", StringComparison.OrdinalIgnoreCase))
                {
                    httpEquiv = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value;
                }
                else if (name.Equals("content", StringComparison.OrdinalIgnoreCase))
                {
                    content = a.Groups[2].Success ? a.Groups[2] : a.Groups[3].Success ? a.Groups[3] : null;
                    contentQuote = a.Groups[2].Success ? "\"" : "'";
                }
            }

            if (content is null || contentQuote is null ||
                !string.Equals(httpEquiv?.Trim(), "content-security-policy", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var original = System.Net.WebUtility.HtmlDecode(content.Value);
            var patched = patch(original);
            if (patched == original) continue;

            var encoded = patched.Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace(contentQuote, contentQuote == "\"" ? "&quot;" : "&#39;", StringComparison.Ordinal);
            sb ??= new StringBuilder(html.Length + 256);
            var start = tag.Index + 5 + content.Index;
            sb.Append(html, copied, start - copied).Append(encoded);
            copied = start + content.Length;
        }

        if (sb is null) return html;
        sb.Append(html, copied, html.Length - copied);
        return sb.ToString();
    }

    /// <summary>
    /// Same, on the raw bytes of the page, so the page's own text is never decoded and re-encoded.
    /// Pages in UTF-16/UTF-32 are not supported (returns null).
    /// </summary>
    public static byte[]? Inject(byte[] html, string css, string script, string nonce) =>
        Inject(html, css, script.Length > 0 ? new[] { script } : Array.Empty<string>(), nonce);

    /// <param name="patchMetaCsp">Given, it is applied to a policy set by a &lt;meta&gt; tag (see <see cref="PatchMetaCsp"/>).</param>
    public static byte[]? Inject(byte[] html, string css, IReadOnlyList<string> scripts, string nonce, Func<string, string>? patchMetaCsp = null)
    {
        var block = BuildBlock(css, scripts, nonce);
        if (block.Length == 0) return html;
        if (html.Length >= 2 && ((html[0] == 0xFF && html[1] == 0xFE) || (html[0] == 0xFE && html[1] == 0xFF))) return null;

        // Latin-1 maps every byte to one char, so the page's own bytes survive unchanged and an index in the text is a byte index.
        var text = Encoding.Latin1.GetString(html);
        var patched = patchMetaCsp is null ? text : PatchMetaCsp(text, patchMetaCsp);
        var at = FindInsertIndex(patched);
        var insert = Encoding.UTF8.GetBytes(block);
        var result = new byte[Encoding.Latin1.GetByteCount(patched) + insert.Length];
        var head = Encoding.Latin1.GetBytes(patched, 0, at, result, 0);
        Buffer.BlockCopy(insert, 0, result, head, insert.Length);
        Encoding.Latin1.GetBytes(patched, at, patched.Length - at, result, head + insert.Length);
        return result;
    }
}
