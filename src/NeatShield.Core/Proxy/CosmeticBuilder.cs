using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeatShield.Core.Proxy;

/// <summary>What is put into a page: one block of CSS and the scripts to run.</summary>
public sealed record CosmeticPayload(string Css, IReadOnlyList<string> Scripts, int ProceduralRules, bool Generic)
{
    public bool IsEmpty => Css.Length == 0 && Scripts.Count == 0;

    public static readonly CosmeticPayload Empty = new("", Array.Empty<string>(), 0, false);
}

/// <summary>Turns the engine's answer for a page (neat_cosmetic_resources) into CSS and scripts.</summary>
public static class CosmeticBuilder
{
    /// <summary>The local address the page script asks for generic class/id selectors. Handled by <see cref="ProxyService"/>.</summary>
    public const string InternalHost = "neatshield.invalid";

    public const string HiddenPath = "/__ns/hidden";

    private static readonly Lazy<string> RuntimeSource = new(LoadRuntime);

    public static string EndpointFor(string pageUrl) =>
        (pageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "http://" : "https://") + InternalHost + HiddenPath;

    /// <param name="json">Answer of the engine for the page URL.</param>
    /// <param name="endpoint">Where the page script sends class and id names (see <see cref="EndpointFor"/>).</param>
    public static CosmeticPayload Build(string? json, string nonce, string endpoint)
    {
        if (string.IsNullOrEmpty(json)) return CosmeticPayload.Empty;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var css = new System.Text.StringBuilder();
        if (root.TryGetProperty("hide_selectors", out var hide) && hide.ValueKind == JsonValueKind.Array)
        {
            css.Append(HtmlInjector.BuildHideCss(hide.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)));
        }

        // Rules that need the page script. Plain "selector => hide/style" ones are cheaper as CSS.
        var rules = new JsonArray();
        if (root.TryGetProperty("procedural_actions", out var proc) && proc.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in proc.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                JsonNode? node;
                try { node = JsonNode.Parse(item.GetString()!); }
                catch (JsonException) { continue; }

                if (node is not JsonObject obj || obj["selector"] is not JsonArray selector || selector.Count == 0) continue;
                if (TryAsCss(obj, selector, out var rule))
                {
                    css.Append(rule).Append('\n');
                }
                else
                {
                    rules.Add(node);
                }
            }
        }

        var generichide = root.TryGetProperty("generichide", out var gh) && gh.ValueKind == JsonValueKind.True;
        var generic = !generichide;

        var exceptions = new JsonArray();
        if (root.TryGetProperty("exceptions", out var exc) && exc.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in exc.EnumerateArray())
            {
                if (x.ValueKind == JsonValueKind.String) exceptions.Add(x.GetString());
            }
        }

        var scripts = new List<string>();
        if (root.TryGetProperty("injected_script", out var inj) && inj.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(inj.GetString()))
        {
            scripts.Add(inj.GetString()!);
        }

        if (rules.Count > 0 || generic)
        {
            var cfg = new JsonObject
            {
                ["rules"] = rules,
                ["generic"] = generic,
                ["endpoint"] = endpoint,
                ["exceptions"] = exceptions,
                ["nonce"] = nonce,
            };
            // The default JSON encoder writes < > & ' and non-ASCII as \uXXXX, so this cannot close the script tag.
            scripts.Add("(" + RuntimeSource.Value + ")(" + cfg.ToJsonString() + ");");
        }

        return new CosmeticPayload(css.ToString(), scripts, rules.Count, generic);
    }

    private const int MaxNames = 5000;
    private const int MaxNameLength = 200;

    /// <summary>
    /// Reads what the page script sent: <c>{"classes":[],"ids":[],"exceptions":[]}</c>. Returns the three arrays as JSON text
    /// for the engine, or null when the body is not what we expect. Lists and names are capped.
    /// </summary>
    public static (string Classes, string Ids, string Exceptions)? ParseHiddenRequest(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            return (Names(doc.RootElement, "classes"), Names(doc.RootElement, "ids"), Names(doc.RootElement, "exceptions"));
        }
        catch (JsonException)
        {
            return null;
        }

        static string Names(JsonElement root, string property)
        {
            var arr = new JsonArray();
            if (root.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in list.EnumerateArray())
                {
                    if (x.ValueKind != JsonValueKind.String) continue;
                    var v = x.GetString();
                    if (string.IsNullOrEmpty(v) || v.Length > MaxNameLength) continue;
                    arr.Add(v);
                    if (arr.Count >= MaxNames) break;
                }
            }

            return arr.ToJsonString();
        }
    }

    private static bool TryAsCss(JsonObject rule, JsonArray selector, out string css)
    {
        css = "";
        if (selector.Count != 1) return false;
        if (selector[0]?["type"]?.GetValue<string>() != "css-selector") return false;
        var sel = selector[0]?["arg"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(sel) || !IsSafe(sel)) return false;

        var action = rule["action"];
        if (action is null)
        {
            css = sel.Trim() + "{display:none!important}";
            return true;
        }

        if (action["type"]?.GetValue<string>() == "style" && action["arg"]?.GetValue<string>() is { } style && IsSafe(style))
        {
            css = sel.Trim() + "{" + style + "}";
            return true;
        }

        return false;
    }

    private static bool IsSafe(string s) => !s.Contains('{') && !s.Contains('}') && !s.Contains("</", StringComparison.Ordinal);

    private static string LoadRuntime()
    {
        using var stream = typeof(CosmeticBuilder).Assembly.GetManifestResourceStream("NeatShield.runtime.js")
                           ?? throw new InvalidOperationException("runtime.js is not embedded in the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
