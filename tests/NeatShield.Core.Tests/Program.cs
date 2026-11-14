// Plain console test runner (no test framework needed): dotnet run --project tests/NeatShield.Core.Tests
using System.Text;
using System.Text.Json;
using NeatShield.Core.Filters;
using NeatShield.Core.Proxy;
using NeatShield.Core.Security;
using NeatShield.Core.Settings;
int fail = 0;

void Eq<T>(T exp, T act, string name){ if(!EqualityComparer<T>.Default.Equals(exp,act)){fail++;Console.WriteLine($"FAIL {name}: expected [{exp}] got [{act}]");} else Console.WriteLine($"ok   {name}"); }
Func<Dictionary<string,string>,Func<string,string?>> H = d => k => d.TryGetValue(k,out var v)?v:null;
Eq("script", RequestClassifier.Classify("https://a.com/x", H(new(){["Sec-Fetch-Dest"]="script"})), "dest script");
Eq("xmlhttprequest", RequestClassifier.Classify("https://a.com/x", H(new(){["Sec-Fetch-Dest"]="empty",["Sec-Fetch-Mode"]="cors"})), "dest empty");
Eq("subdocument", RequestClassifier.Classify("https://a.com/x", H(new(){["Sec-Fetch-Dest"]="iframe"})), "iframe");
Eq("document", RequestClassifier.Classify("https://a.com/x", H(new(){["Sec-Fetch-Mode"]="navigate"})), "navigate");
Eq("websocket", RequestClassifier.Classify("https://a.com/x", H(new(){["Upgrade"]="WebSocket"})), "ws");
Eq("image", RequestClassifier.Classify("https://a.com/p.PNG?x=1.js", H(new())), "ext image");
Eq("script", RequestClassifier.Classify("https://a.com/p.js#a.png", H(new())), "ext js");
Eq("document", RequestClassifier.Classify("https://a.com/x", H(new(){["Accept"]="text/html,application/xhtml"})), "accept html");
Eq("other", RequestClassifier.Classify("https://a.com/x", H(new())), "other");
Eq("https://r.com/p", RequestClassifier.SourceUrl("https://a.com/x","script",H(new(){["Referer"]="https://r.com/p"})), "referer");
Eq("https://o.com", RequestClassifier.SourceUrl("https://a.com/x","script",H(new(){["Origin"]="https://o.com"})), "origin");
Eq("https://a.com/x", RequestClassifier.SourceUrl("https://a.com/x","document",H(new())), "self source");
var ex = new[]{"example.org","*.bank.com"};
Eq(true, HostRules.ShouldDecrypt("www.news.com", ex), "decrypt news");
Eq(false, HostRules.ShouldDecrypt("sub.example.org:443", ex), "excl example");
Eq(false, HostRules.ShouldDecrypt("bank.com", ex), "excl wildcard apex");
Eq(false, HostRules.ShouldDecrypt("notbank.com.evil", new[]{"bank.com"}) ? false:true, "no false suffix"); // evil.com not excluded → decrypt true → expression false==false? 
Eq(true, HostRules.ShouldDecrypt("notbank.com", new[]{"bank.com"}), "notbank not matched");
Eq(false, HostRules.ShouldDecrypt("192.168.1.1", ex), "ip");
Eq(false, HostRules.ShouldDecrypt("localhost", ex), "localhost");
Eq(false, HostRules.ShouldDecrypt("[::1]:443", ex), "ipv6");
Eq("a.com", HostRules.HostOf("https://A.com:8443/x"), "hostof");
var n = "N0nce";
Eq("script-src 'self' 'nonce-N0nce'; img-src *", CspNonce.AddNonce("script-src 'self'; img-src *", n).Replace("; ","; ").Trim().Replace(" ;",";").Replace("  "," ").Replace("script-src","script-src").TrimStart(), "csp add");
Eq("script-src 'unsafe-inline'", CspNonce.AddNonce("script-src 'unsafe-inline'", n).Trim(), "csp unsafe-inline untouched");
Console.WriteLine(CspNonce.AddNonce("default-src 'none'; script-src 'sha256-abc' 'unsafe-inline' 'strict-dynamic'; style-src 'self'", n));
var html = "<!DOCTYPE html><html><HEADER>x</HEADER><head lang=x><title>t</title></head><body></body></html>";
var o = HtmlInjector.Inject(html, "a{display:none!important}", "alert(1)</script>", n);
Console.WriteLine(o);
Eq(true, o.Contains("<head lang=x><style nonce=\"N0nce\">"), "inject after head");
Eq(true, o.Contains("<\\/script>"), "script escaped");
Eq("<p>x</p>", HtmlInjector.Inject("<p>x</p>","","",n), "nothing to inject");
Eq(true, HtmlInjector.Inject("<p>x</p>","a{}","",n).StartsWith("<style"), "fragment prepend");
Eq(true, HtmlInjector.Inject("<!doctype html><p>x</p>","a{}","",n).StartsWith("<!doctype html><style"), "after doctype");
Eq("a{display:none!important}\n", HtmlInjector.BuildHideCss(new[]{"a","b{x}","</style>"}).Replace("b{x}",""), "hide css filter");
Eq(true, DataUrl.TryParse("data:text/javascript;base64,KGZ1bmN0aW9uKCl7fSkoKQ==", out var p) && p.ContentType=="text/javascript" && p.Body.Length==16, "dataurl b64");
Eq(true, DataUrl.TryParse("data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7", out var g) && g.Body.Length==42, "dataurl gif");
Eq(true, DataUrl.TryParse("data:text/plain,hi%20there", out var t) && System.Text.Encoding.UTF8.GetString(t.Body)=="hi there", "dataurl plain");
Eq(false, DataUrl.TryParse("http://x", out _), "not data");
var raw = System.Text.Encoding.GetEncoding("iso-8859-1").GetBytes("<html><head><title>caf\u00e9</title></head><body>\u00e9</body></html>");
var ob = HtmlInjector.Inject(raw, "a{display:none}", "", n)!;
Eq(raw.Length + System.Text.Encoding.UTF8.GetByteCount(HtmlInjector.BuildBlock("a{display:none}","",n)), ob.Length, "bytes len");
var tail = System.Text.Encoding.Latin1.GetString(ob);
Eq(true, tail.Contains("<head><style nonce=") && tail.Contains("<title>caf\u00e9</title>"), "bytes untouched text");
Eq(true, HtmlInjector.Inject(new byte[]{0xFF,0xFE,0x3C,0},"a{}","",n) is null, "utf16 refused");

// ---- manifest ----
var sample = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", "manifest.sample.json"));
var mf = FilterManifest.Parse(sample); mf.Validate();
Eq(1, mf.Schema, "manifest schema");
Eq(true, mf.Files.Count > 0 && mf.NetworkRules > 0, "manifest files+rules");
string Mut(Func<JsonElement, string> f) { using var d = JsonDocument.Parse(sample); return f(d.RootElement); }
bool Rejects(string json) { try { FilterManifest.Parse(Encoding.UTF8.GetBytes(json)).Validate(); return false; } catch (Exception) { return true; } }
Eq(true, Rejects("{\"schema\":2,\"version\":\"1\",\"files\":[]}"), "manifest rejects schema 2");
Eq(true, Rejects("not json"), "manifest rejects garbage");
var text = Encoding.UTF8.GetString(sample);
Eq(true, Rejects(text.Replace("\"schema\": 1", "\"schema\": 9").Replace("\"schema\":1", "\"schema\":9")), "manifest rejects other schema");
var firstName = mf.Files[0].Name;
Eq(true, Rejects(text.Replace("\"" + firstName + "\"", "\"../evil.txt\"")), "manifest rejects path traversal");
Eq(true, Rejects(text.Replace(mf.Files[0].Sha256, "abc")), "manifest rejects short sha256");

// ---- settings ----
var st = new AppSettings { Port = 80, UpdateIntervalHours = 0, FiltersBaseUrl = "http://evil.example/x", ExcludedHosts = new() { "B.com", "b.com", " ", "*.C.com" } };
st.Normalize();
Eq(true, st.Port >= 1024, "port clamped");
Eq(6, st.UpdateIntervalHours, "interval default");
Eq(true, st.FiltersBaseUrl.StartsWith("https://") && st.FiltersBaseUrl.EndsWith("/"), "base url https");
Eq(2, st.ExcludedHosts.Count, "hosts dedup");

// ---- secret store ----
var sec = SecretStore.NewSecret();
Eq(sec, SecretStore.Unprotect(SecretStore.Protect(sec)), "secret roundtrip");
Eq(true, SecretStore.Unprotect("garbage") is null && SecretStore.Unprotect(null) is null, "secret bad input");

// ---- system proxy recognition ----
Eq(true, SystemProxy.LooksLikeOurs("127.0.0.1:8877"), "proxy ours simple");
Eq(true, SystemProxy.LooksLikeOurs("http=127.0.0.1:8877;https=127.0.0.1:8877"), "proxy ours per-protocol");
Eq(false, SystemProxy.LooksLikeOurs("proxy.corp:8080"), "proxy not ours");
Eq(false, SystemProxy.LooksLikeOurs("192.168.1.1:127"), "proxy not ours 2");

// ---- popup detection ----
string pop(Dictionary<string,string> h, string url="https://ads.example/x", string m="GET") => PopupDetector.TryGetOpener(url, m, H(h), out var o) ? o : "NO";
Eq("https://news.example/a", pop(new(){["Sec-Fetch-Dest"]="document",["Sec-Fetch-Mode"]="navigate",["Sec-Fetch-Site"]="cross-site",["Referer"]="https://news.example/a"}), "popup cross-site with referer");
Eq("https://ads.example/x", pop(new(){["Sec-Fetch-Dest"]="document",["Sec-Fetch-Site"]="cross-site"}), "popup cross-site no referer -> self");
Eq("NO", pop(new(){["Sec-Fetch-Dest"]="document",["Sec-Fetch-Site"]="same-site",["Referer"]="https://a.ads.example/"}), "same-site is not popup");
Eq("NO", pop(new(){["Sec-Fetch-Dest"]="document",["Sec-Fetch-Site"]="none"}), "typed address is not popup");
Eq("NO", pop(new(){["Sec-Fetch-Dest"]="iframe",["Sec-Fetch-Site"]="cross-site"}), "iframe is not popup");
Eq("NO", pop(new(){["Sec-Fetch-Dest"]="script",["Sec-Fetch-Site"]="cross-site"}), "script is not popup");
Eq("NO", pop(new(){["Sec-Fetch-Dest"]="document",["Sec-Fetch-Site"]="cross-site"}, m:"POST"), "POST is not popup");
Eq("https://news.example/", pop(new(){["Accept"]="text/html",["Referer"]="https://news.example/"}), "old browser: referer differs");
Eq("NO", pop(new(){["Accept"]="text/html",["Referer"]="https://www.ads.example/p"}), "old browser: same site");
Eq("NO", pop(new(){["Accept"]="text/html"}), "old browser: no referer");
Eq(true, HostRules.IsExcluded("sub.good.com", new[]{"good.com"}) && !HostRules.IsExcluded("good.org", new[]{"good.com"}), "IsExcluded");

// ---- cosmetic builder ----
var cj = """{"hide_selectors":[".a","b{x}"],"procedural_actions":["{\"selector\":[{\"type\":\"css-selector\",\"arg\":\".p1\"}]}","{\"selector\":[{\"type\":\"css-selector\",\"arg\":\".p2\"}],\"action\":{\"type\":\"style\",\"arg\":\"margin: 0 !important\"}}","{\"selector\":[{\"type\":\"css-selector\",\"arg\":\".p3\"},{\"type\":\"has-text\",\"arg\":\"</script><b>x\"}]}","{\"selector\":[{\"type\":\"css-selector\",\"arg\":\".p4\"}],\"action\":{\"type\":\"remove\"}}","not json"],"exceptions":[".ex"],"injected_script":"window.__s=1;","generichide":false}""";
var ep = CosmeticBuilder.EndpointFor("https://a.com/");
var pl = CosmeticBuilder.Build(cj, "N0nce", ep);
Eq("https://neatshield.invalid/__ns/hidden", ep, "endpoint https");
Eq("http://neatshield.invalid/__ns/hidden", CosmeticBuilder.EndpointFor("http://a.com/"), "endpoint http");
Eq(true, pl.Css.Contains(".a{display:none!important}") && !pl.Css.Contains("b{x}"), "css hide selectors");
Eq(true, pl.Css.Contains(".p1{display:none!important}") && pl.Css.Contains(".p2{margin: 0 !important}"), "pure-css procedural -> css");
Eq(2, pl.ProceduralRules, "js rules = has-text + remove");
Eq(2, pl.Scripts.Count, "scriptlet + runtime");
Eq("window.__s=1;", pl.Scripts[0], "scriptlet first");
Eq(true, pl.Scripts[1].Contains("\"endpoint\":\"https://neatshield.invalid/__ns/hidden\"") && pl.Scripts[1].Contains("\"generic\":true"), "runtime config");
Eq(false, pl.Scripts[1].Contains("</script"), "no closing script tag leaks from rule text");
var block = HtmlInjector.BuildBlock(pl.Css, pl.Scripts, "N0nce");
Eq(2, System.Text.RegularExpressions.Regex.Matches(block, "<script nonce").Count, "one tag per script");
var gh = CosmeticBuilder.Build("""{"hide_selectors":[],"procedural_actions":[],"exceptions":[],"injected_script":"","generichide":true}""", "n", ep);
Eq(true, gh.IsEmpty, "generichide + no rules -> nothing");
var onlyGeneric = CosmeticBuilder.Build("""{"hide_selectors":[],"procedural_actions":[],"exceptions":[],"injected_script":"","generichide":false}""", "n", ep);
Eq(true, onlyGeneric.Scripts.Count == 1 && onlyGeneric.Generic, "generic only -> runtime only");
Eq(true, CosmeticBuilder.Build("", "n", ep).IsEmpty && CosmeticBuilder.Build(null, "n", ep).IsEmpty, "empty json");

var hr = CosmeticBuilder.ParseHiddenRequest("""{"classes":["a","b",1,""],"ids":["x"],"exceptions":[".e"]}""");
Eq("[\"a\",\"b\"]", hr?.Classes, "hidden request classes");
Eq("[\"x\"]", hr?.Ids, "hidden request ids");
Eq(true, CosmeticBuilder.ParseHiddenRequest("nope") is null && CosmeticBuilder.ParseHiddenRequest("[1]") is null, "hidden request bad");
Eq("[\"x\"]", CosmeticBuilder.ParseHiddenRequest("{\"classes\":[\"" + new string('z', 201) + "\",\"x\"]}")?.Classes, "long names dropped");

// ---- csp connect ----
var origin = "https://neatshield.invalid";
Eq("default-src 'self' https://neatshield.invalid", CspNonce.AllowConnect("default-src 'self'", origin), "connect via default-src");
Eq("connect-src 'self' https://neatshield.invalid; img-src *", CspNonce.AllowConnect("connect-src 'self'; img-src *", origin).Replace(";  ", "; "), "connect-src");
Eq("img-src *", CspNonce.AllowConnect("img-src *", origin), "no connect/default: unchanged");
Eq("connect-src *", CspNonce.AllowConnect("connect-src *", origin), "wildcard unchanged");
Eq(true, CspNonce.AllowConnect("connect-src 'none'", origin).Contains(origin) && !CspNonce.AllowConnect("connect-src 'none'", origin).Contains("'none'"), "none replaced");

// ---- custom filters ----
var cm = FilterManifest.Parse(Encoding.UTF8.GetBytes(ComposedRelease.ComposeManifestForTest(sample, "||x.example^\n")));
Eq(true, cm.Files.Any(f => f.Name == "custom.txt"), "composed manifest lists custom.txt");
var tmpRel = Path.Combine(Path.GetTempPath(), "ns-rel-" + Guid.NewGuid().ToString("N")[..6]);
Directory.CreateDirectory(tmpRel);
File.WriteAllBytes(Path.Combine(tmpRel, "manifest.json"), sample);
File.WriteAllText(Path.Combine(tmpRel, "a.txt"), "a");
var comp = Path.Combine(tmpRel, "..", "ns-comp-" + Guid.NewGuid().ToString("N")[..6]);
Eq(tmpRel, ComposedRelease.Build(tmpRel, "   \n", comp), "no custom rules -> same dir");
var d1 = ComposedRelease.Build(tmpRel, "||x.example^\r\n", comp);
Eq(true, File.Exists(Path.Combine(d1, "a.txt")) && File.ReadAllText(Path.Combine(d1, "custom.txt")) == "||x.example^\n", "composed dir content");
Eq(d1, ComposedRelease.Build(tmpRel, "||x.example^\n", comp), "same rules -> same dir (reused)");
var d2 = ComposedRelease.Build(tmpRel, "||y.example^\n", comp);
Eq(true, d1 != d2 && !Directory.Exists(d1) && Directory.Exists(d2), "changed rules -> new dir, old pruned");
Eq(true, File.ReadAllBytes(Path.Combine(tmpRel, "manifest.json")).SequenceEqual(sample), "release manifest untouched");
var st2 = new AppSettings { CustomFilters = "a\r\nb" + new string('x', 300000) };
st2.Normalize();
Eq(true, st2.CustomFilters.StartsWith("a\nb") && st2.CustomFilters.Length == AppSettings.MaxCustomFiltersLength, "custom filters normalized+capped");
Eq("", new AppSettings { Language = "fr" }.Also(x => x.Normalize()).Language, "language normalized");
// ---- policy in a <meta> tag ----
Func<string, string> addN = pol => CspNonce.AllowConnect(CspNonce.AddNonce(pol, "N1"), "https://neatshield.invalid");
var m1 = HtmlInjector.PatchMetaCsp("<head><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'self'; script-src 'self'\"><title>x</title>", addN);
Eq(true, m1.Contains("script-src 'self' 'nonce-N1'") && m1.Contains("default-src 'self' 'nonce-N1' https://neatshield.invalid") && m1.EndsWith("<title>x</title>"), "meta csp patched (double quotes)");
var m2 = HtmlInjector.PatchMetaCsp("<META CONTENT='default-src &#39;self&#39; &amp; x' HTTP-EQUIV=content-security-policy>", addN);
Eq("<META CONTENT='default-src &#39;self&#39; &amp; x &#39;nonce-N1&#39; https://neatshield.invalid' HTTP-EQUIV=content-security-policy>", m2, "meta csp patched (single quotes, entities, odd order)");
var plainMeta = "<meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><meta http-equiv=\"refresh\" content=\"5\">";
Eq(true, ReferenceEquals(plainMeta, HtmlInjector.PatchMetaCsp(plainMeta, addN)), "other meta tags untouched");
var far = new string(' ', 200000) + "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'self'\">";
Eq(true, ReferenceEquals(far, HtmlInjector.PatchMetaCsp(far, addN)), "meta far from the start ignored");
Eq("<p>no meta</p>", HtmlInjector.PatchMetaCsp("<p>no meta</p>", addN), "no meta");
var twoMeta = HtmlInjector.PatchMetaCsp("<meta http-equiv=\"content-security-policy\" content=\"script-src 'self'\"><meta http-equiv=\"Content-Security-Policy\" content=\"style-src 'self'\">", addN);
Eq(true, twoMeta.Contains("script-src 'self' 'nonce-N1'") && twoMeta.Contains("style-src 'self' 'nonce-N1'"), "two meta policies");
var withMeta = System.Text.Encoding.UTF8.GetBytes("<!doctype html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'\"><title>Tiêu đề</title></head><body>xin chào</body></html>");
var outMeta = HtmlInjector.Inject(withMeta, "a{display:none}", new[] { "1+1" }, "N1", addN)!;
var outText = System.Text.Encoding.UTF8.GetString(outMeta);
Eq(true, outText.Contains("'nonce-N1' https://neatshield.invalid\"") && outText.Contains("Tiêu đề") && outText.Contains("xin chào") && outText.Contains("<head><style nonce=\"N1\">"), "bytes: meta patched, page text and block intact");
var noMetaOut = HtmlInjector.Inject(System.Text.Encoding.UTF8.GetBytes("<head><title>Tiêu đề</title></head>"), "a{}", new[] { "x" }, "N1", addN)!;
Eq(true, System.Text.Encoding.UTF8.GetString(noMetaOut).Contains("Tiêu đề"), "bytes: utf-8 text survives without a meta policy");

// ---- pages in two languages ----
var bp = PageText.BlockedPage("https://evil.example/<script>alert(1)</script>", "en");
Eq(true, bp.Contains("lang=\"en\"") && bp.Contains("This page was blocked") && !bp.Contains("<script>alert"), "blocked page en, url encoded");
Eq(true, PageText.BlockedPage("https://a.example/", "vi").Contains("Trang này đã bị chặn"), "blocked page vi");
Eq(true, PageText.BlockedPage("https://a.example/" + new string('a', 500), "en").Length < 3000, "blocked page url shortened");
var pp = PageText.PopupPage("https://pop.example/", "en");
Eq(true, pp.Contains("Pop-up blocked by NeatShield") && pp.Contains("window.close()"), "popup page en");
Eq(true, PageText.PopupPage("https://pop.example/", "vi").Contains("Đã chặn popup"), "popup page vi");
Eq("vi", new AppSettings { Language = "vi" }.EffectiveLanguage, "language vi");
Eq("en", new AppSettings { Language = "en" }.EffectiveLanguage, "language en");
Eq(true, new AppSettings().EffectiveLanguage is "vi" or "en", "language auto");

// ---- window texts ----
var texts = (System.Collections.IDictionary)typeof(NeatShield.App.Loc).GetField("Texts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
var badTexts = new List<string>();
foreach (System.Collections.DictionaryEntry de in texts)
{
    var (vi, en) = ((string, string))de.Value!;
    var ph = new Func<string, string>(t => string.Join(",", System.Text.RegularExpressions.Regex.Matches(t, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x)));
    if (string.IsNullOrWhiteSpace(vi) || string.IsNullOrWhiteSpace(en) || ph(vi) != ph(en)) badTexts.Add((string)de.Key);
}
Eq("", string.Join(" ", badTexts), "every text has vi+en with same placeholders");
NeatShield.App.Loc.Language = "vi";
Eq("Bộ lọc", NeatShield.App.Loc.T("nav.filters"), "Loc vi");
NeatShield.App.Loc.Language = "en";
Eq("Filters", NeatShield.App.Loc.T("nav.filters"), "Loc en");
Eq("no.such.key", NeatShield.App.Loc.T("no.such.key"), "Loc unknown key");
Eq("Version: 7", NeatShield.App.Loc.T("filters.version", 7), "Loc format");
Eq("Downloading a.txt (2/5)...", NeatShield.App.Loc.Progress("Downloading a.txt (2/5)..."), "progress en");
NeatShield.App.Loc.Language = "vi";
Eq("Đang tải a.txt (2/5)...", NeatShield.App.Loc.Progress("Downloading a.txt (2/5)..."), "progress vi");
Eq("Đang tải manifest.json...", NeatShield.App.Loc.Progress("Downloading manifest.json..."), "progress manifest vi");
Eq("something else", NeatShield.App.Loc.Progress("something else"), "progress passthrough");
var langChanged = 0;
EventHandler onLang = (_, _) => langChanged++;
NeatShield.App.Loc.Changed += onLang;
NeatShield.App.Loc.Language = "vi"; NeatShield.App.Loc.Language = "en"; NeatShield.App.Loc.Language = "en";
NeatShield.App.Loc.Changed -= onLang;
Eq(1, langChanged, "Loc.Changed only on a real change");
Console.WriteLine(fail==0?"ALL OK":$"{fail} FAILED");
return fail;

static class Ext { public static T Also<T>(this T x, Action<T> a) { a(x); return x; } }
