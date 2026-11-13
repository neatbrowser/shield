using System.Net;
using System.Text;
using System.Text.Json;
using Neat.Adblock;
using NeatShield.Core.Engine;
using NeatShield.Core.Logging;
using NeatShield.Core.Security;
using NeatShield.Core.Settings;
using NeatShield.Core.Stats;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace NeatShield.Core.Proxy;

public enum ProxyState { Stopped, Running, Failed }

/// <summary>
/// The local proxy. Every request from the browser goes through <see cref="OnBeforeRequest"/>, where the filter engine
/// decides to let it pass, block it or answer it with a harmless stand-in. HTML pages get their hide-rules and
/// scripts added in <see cref="OnBeforeResponse"/>.
/// </summary>
public sealed class ProxyService : IDisposable
{
    private const int MaxHtmlBytes = 6 * 1024 * 1024;

    private static readonly byte[] TransparentGif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    private readonly SettingsStore _settings;
    private readonly EngineHost _engine;
    private readonly StatsCollector _stats;
    private readonly ProxyServer _server;
    private readonly object _gate = new();
    private ExplicitProxyEndPoint? _endpoint;
    private bool _disposed;

    /// <summary>What we remember about a request until its response comes back.</summary>
    private sealed record PageRequest(string Url, string Source, string Type);

    public ProxyService(SettingsStore settings, EngineHost engine, StatsCollector stats)
    {
        _settings = settings;
        _engine = engine;
        _stats = stats;
        // We install and remove trust ourselves (CertificateService), so Titanium must not do it on its own.
        _server = new ProxyServer(userTrustRootCertificate: false, machineTrustRootCertificate: false, trustRootCertificateAsAdmin: false);
        _server.BeforeRequest += OnBeforeRequest;
        _server.BeforeResponse += OnBeforeResponse;
    }

    public ProxyState State { get; private set; } = ProxyState.Stopped;

    public string? LastError { get; private set; }

    public int Port { get; private set; }

    public event EventHandler? StateChanged;

    /// <summary>The certificate manager of the proxy, handed to <see cref="CertificateService"/>.</summary>
    public Titanium.Web.Proxy.Network.CertificateManager CertificateManager => _server.CertificateManager;

    /// <summary>Starts listening on 127.0.0.1. The system proxy setting is not touched here.</summary>
    public void Start(Action ensureCertificate)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (State == ProxyState.Running) return;

            try
            {
                ensureCertificate();
                var port = _settings.Current.Port;
                var endpoint = new ExplicitProxyEndPoint(IPAddress.Loopback, port, decryptSsl: true);
                endpoint.BeforeTunnelConnectRequest += OnTunnelConnect;
                _server.AddEndPoint(endpoint);
                _endpoint = endpoint;
                try
                {
                    _server.Start(changeSystemProxySettings: false);
                }
                catch
                {
                    _server.ProxyEndPoints.Remove(endpoint);
                    _endpoint = null;
                    throw;
                }

                Port = port;
                LastError = null;
                SetState(ProxyState.Running);
                Log.Info($"proxy listening on 127.0.0.1:{port}");
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Error($"proxy could not start: {ex}");
                SetState(ProxyState.Failed);
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_endpoint is null && State != ProxyState.Running)
            {
                if (State == ProxyState.Failed) SetState(ProxyState.Stopped);
                return;
            }

            try
            {
                _server.Stop();
            }
            catch (Exception ex)
            {
                Log.Warn($"proxy stop: {ex.Message}");
            }

            if (_endpoint is not null)
            {
                _server.ProxyEndPoints.Remove(_endpoint);
                _endpoint = null;
            }

            SetState(ProxyState.Stopped);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
            _server.Dispose();
        }
    }

    private void SetState(ProxyState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- HTTPS: which sites are opened up ----

    private Task OnTunnelConnect(object sender, TunnelConnectSessionEventArgs e)
    {
        var request = e.HttpClient.Request;
        var host = HostRules.Normalize(request.RequestUri?.Host ?? request.Host);
        var s = _settings.Current;
        e.DecryptSsl = s.ProtectionEnabled
                       && s.FilterHttps
                       && !MitmExclusionDefaults.IsBuiltInSslBypass(host)
                       && HostRules.ShouldDecrypt(host, s.ExcludedHosts);
        return Task.CompletedTask;
    }

    // ---- Requests ----

    private Task OnBeforeRequest(object sender, SessionEventArgs e)
    {
        try
        {
            Handle(e);
        }
        catch (Exception ex)
        {
            // A problem in filtering must never break browsing: let the request through.
            Log.Warn($"request filter error: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private void Handle(SessionEventArgs e)
    {
        var request = e.HttpClient.Request;
        _stats.OnRequest();

        var engine = _engine.Current;
        if (engine is null || !_settings.Current.ProtectionEnabled) return;

        var url = request.Url;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? Header(string name) => request.Headers.GetFirstHeader(name)?.Value;
        var type = RequestClassifier.Classify(url, Header);
        var source = RequestClassifier.SourceUrl(url, type, Header);
        var host = HostRules.HostOf(url);
        var isPage = type is "document" or "subdocument";

        var settings = _settings.Current;
        if (type == "document" && settings.BlockPopups
            && PopupDetector.TryGetOpener(url, request.Method, Header, out var opener)
            && !HostRules.IsExcluded(host, settings.ExcludedHosts)
            && !HostRules.IsExcluded(HostRules.HostOf(opener), settings.ExcludedHosts)
            && engine.ShouldBlockPopup(url, opener))
        {
            _stats.OnPopupBlocked(host);
            e.GenericResponse(PopupPage(url), HttpStatusCode.OK, new[]
            {
                new HttpHeader("Content-Type", "text/html; charset=utf-8"),
                new HttpHeader("Cache-Control", "no-store"),
            });
            return;
        }

        var result = engine.Check(url, source, type, request.Method);
        if (result.Action == AdblockAction.Allow && type == "document" && request.Method == "GET" && engine.ShouldBlockDocument(url))
        {
            result = new CheckResult(AdblockAction.Block, null, null);
        }

        switch (result.Action)
        {
            case AdblockAction.Block:
                _stats.OnBlocked(host, type);
                RespondBlocked(e, type, url);
                return;

            case AdblockAction.Redirect when DataUrl.TryParse(result.Redirect, out var data):
                _stats.OnRedirected(host, type);
                e.Ok(data.Body, new[]
                {
                    new HttpHeader("Content-Type", data.ContentType),
                    new HttpHeader("Cache-Control", "no-store"),
                });
                return;

            case AdblockAction.Redirect:
                // A redirect we cannot read: block it, which is what the rule's author wanted at the least.
                _stats.OnBlocked(host, type);
                RespondBlocked(e, type, url);
                return;
        }

        if (result.RewrittenUrl is { Length: > 0 } rewritten && Uri.TryCreate(rewritten, UriKind.Absolute, out var newUri)
            && Uri.TryCreate(url, UriKind.Absolute, out var oldUri)
            && string.Equals(newUri.Host, oldUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            // Same site, different query (removeparam). The request line is "GET /path?x HTTP/1.1" inside a decrypted
            // tunnel and "GET http://host/path?x" for plain http through the proxy; keep whichever form it had.
            request.RequestUriString = request.RequestUriString.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? rewritten
                : newUri.PathAndQuery;
            _stats.OnRewritten();
        }

        if (isPage)
        {
            e.UserData = new PageRequest(url, source, type);
        }
    }

    private static void RespondBlocked(SessionEventArgs e, string type, string url)
    {
        var noStore = new HttpHeader("Cache-Control", "no-store");
        switch (type)
        {
            case "document":
                e.GenericResponse(BlockPage(url), HttpStatusCode.Forbidden, new[]
                {
                    new HttpHeader("Content-Type", "text/html; charset=utf-8"),
                    noStore,
                });
                break;
            case "subdocument":
                e.Ok(Encoding.UTF8.GetBytes("<!doctype html><title></title>"), new[]
                {
                    new HttpHeader("Content-Type", "text/html; charset=utf-8"),
                    noStore,
                });
                break;
            case "script":
                e.Ok(Encoding.UTF8.GetBytes("/* blocked by NeatShield */"), new[]
                {
                    new HttpHeader("Content-Type", "application/javascript"),
                    noStore,
                });
                break;
            case "stylesheet":
                e.Ok(Encoding.UTF8.GetBytes("/* blocked by NeatShield */"), new[]
                {
                    new HttpHeader("Content-Type", "text/css"),
                    noStore,
                });
                break;
            case "image":
                e.Ok(TransparentGif, new[]
                {
                    new HttpHeader("Content-Type", "image/gif"),
                    noStore,
                });
                break;
            case "websocket":
                e.GenericResponse(Array.Empty<byte>(), HttpStatusCode.Forbidden, new[] { noStore });
                break;
            default:
                e.GenericResponse(Array.Empty<byte>(), HttpStatusCode.NoContent, new[] { noStore });
                break;
        }
    }

    /// <summary>
    /// Answer for a blocked pop-up: the new window closes itself. If it was not a new window (a link opened in the
    /// same tab), the visitor sees why the page is empty and can go back.
    /// </summary>
    private static byte[] PopupPage(string url)
    {
        var shown = WebUtility.HtmlEncode(url.Length > 300 ? url[..300] + "…" : url);
        var html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Pop-up blocked</title>"
                   + "<style>body{font:16px/1.5 system-ui,Segoe UI,sans-serif;margin:32px;color:#1b2430}code{word-break:break-all;font-size:13px;color:#555}</style></head>"
                   + "<body><h2>Pop-up blocked by NeatShield</h2><p><code>" + shown + "</code></p>"
                   + "<p>This address is on the pop-up list. If you opened it on purpose, add its host to the exclusions in NeatShield settings.</p>"
                   + "<p><a href=\"javascript:history.back()\">Go back</a></p>"
                   + "<script>try{window.close()}catch(e){}</script></body></html>";
        return Encoding.UTF8.GetBytes(html);
    }

    private static byte[] BlockPage(string url)
    {
        var shown = WebUtility.HtmlEncode(url.Length > 300 ? url[..300] + "…" : url);
        var html = "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
                   + "<title>Blocked by NeatShield</title><style>body{font:16px/1.5 system-ui,Segoe UI,sans-serif;background:#f4f6f8;color:#1b2430;"
                   + "display:grid;place-items:center;min-height:100vh;margin:0}main{max-width:560px;padding:32px;background:#fff;border-radius:12px;"
                   + "box-shadow:0 2px 12px #0002}h1{margin:0 0 8px;font-size:22px;color:#c0392b}code{word-break:break-all;font-size:13px;color:#555}</style></head>"
                   + "<body><main><h1>This page was blocked</h1><p>NeatShield stopped this address because it is on a filter list "
                   + "(malware, phishing or tracking).</p><p><code>" + shown + "</code></p>"
                   + "<p>If you trust this site, add its host to the exclusions in NeatShield settings.</p></main></body></html>";
        return Encoding.UTF8.GetBytes(html);
    }

    // ---- Responses ----

    private async Task OnBeforeResponse(object sender, SessionEventArgs e)
    {
        if (e.UserData is not PageRequest page) return;
        var engine = _engine.Current;
        if (engine is null) return;

        try
        {
            var response = e.HttpClient.Response;
            if (_settings.Current.CosmeticFiltering && IsInjectable(response))
            {
                var (css, script) = BuildCosmetic(engine, page.Url);
                if (css.Length > 0 || script.Length > 0)
                {
                    var body = await e.GetResponseBody();
                    if (body.Length > 0 && body.Length <= MaxHtmlBytes)
                    {
                        var nonce = CspNonce.NewNonce();
                        var changed = HtmlInjector.Inject(body, css, script, nonce);
                        if (changed is not null)
                        {
                            e.SetResponseBody(changed);
                            AddNonceToCsp(response, nonce);
                            _stats.OnPageCleaned();
                        }
                    }
                }
            }

            // $csp rules add a policy of their own (after the nonce step, so the nonce does not weaken it).
            var csp = engine.GetCsp(page.Url, page.Source, page.Type);
            if (!string.IsNullOrEmpty(csp))
            {
                response.Headers.AddHeader("Content-Security-Policy", csp);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"response filter error: {ex.Message}");
        }
    }

    private static bool IsInjectable(Response response)
    {
        if (response.StatusCode is < 200 or >= 300 || response.StatusCode == 204) return false;
        if (response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) != true) return false;
        if (response.ContentLength > MaxHtmlBytes) return false;

        // Only encodings Titanium can unpack and pack again.
        var encoding = response.ContentEncoding?.Trim().ToLowerInvariant();
        return encoding is null or "" or "identity" or "gzip" or "deflate" or "br";
    }

    private static (string css, string script) BuildCosmetic(AdblockEngine engine, string url)
    {
        var json = engine.CosmeticResourcesJson(url);
        if (string.IsNullOrEmpty(json)) return ("", "");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var css = "";
        if (root.TryGetProperty("hide_selectors", out var selectors) && selectors.ValueKind == JsonValueKind.Array)
        {
            css = HtmlInjector.BuildHideCss(selectors.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!));
        }

        var script = root.TryGetProperty("injected_script", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString() ?? ""
            : "";
        return (css, script);
    }

    /// <summary>Adds our nonce to every enforced Content-Security-Policy so the tags we insert are allowed to run.</summary>
    private static void AddNonceToCsp(Response response, string nonce)
    {
        const string name = "Content-Security-Policy";
        var headers = response.Headers.GetHeaders(name);
        if (headers is null || headers.Count == 0) return;

        var changed = headers.Select(h => CspNonce.AddNonce(h.Value, nonce)).ToList();
        response.Headers.RemoveHeader(name);
        foreach (var value in changed)
        {
            response.Headers.AddHeader(name, value);
        }
    }
}
