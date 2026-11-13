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
            if (++count >= 5000) break;
        }

        return sb.ToString();
    }

    /// <summary>The style and script tags for a page, or "" when there is nothing to add.</summary>
    public static string BuildBlock(string css, string script, string nonce)
    {
        var block = new StringBuilder();
        if (css.Length > 0)
        {
            block.Append("<style nonce=\"").Append(nonce).Append("\">")
                .Append(css.Replace("</style", "<\\/style", StringComparison.OrdinalIgnoreCase))
                .Append("</style>");
        }

        if (script.Length > 0)
        {
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
    /// Same, on the raw bytes of the page, so the page's own text is never decoded and re-encoded.
    /// Pages in UTF-16/UTF-32 are not supported (returns null).
    /// </summary>
    public static byte[]? Inject(byte[] html, string css, string script, string nonce)
    {
        var block = BuildBlock(css, script, nonce);
        if (block.Length == 0) return html;
        if (html.Length >= 2 && ((html[0] == 0xFF && html[1] == 0xFE) || (html[0] == 0xFE && html[1] == 0xFF))) return null;

        // Latin-1 maps every byte to one char, so the index found in the text is the byte index.
        var at = FindInsertIndex(Encoding.Latin1.GetString(html));
        var insert = Encoding.UTF8.GetBytes(block);
        var result = new byte[html.Length + insert.Length];
        Buffer.BlockCopy(html, 0, result, 0, at);
        Buffer.BlockCopy(insert, 0, result, at, insert.Length);
        Buffer.BlockCopy(html, at, result, at + insert.Length, html.Length - at);
        return result;
    }
}
