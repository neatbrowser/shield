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
Console.WriteLine(fail==0?"ALL OK":$"{fail} FAILED");
return fail;
