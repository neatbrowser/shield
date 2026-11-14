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

    private async Task OnBeforeRequest(object sender, SessionEventArgs e)
    {
        try
        {
            if (HostRules.HostOf(e.HttpClient.Request.Url) == CosmeticBuilder.InternalHost)
            {
                await HandleInternal(e);
                return;
            }

            Handle(e);
        }
        catch (Exception ex)
        {
            // A problem in filtering must never break browsing: let the request through.
            Log.Warn($"request filter error: {ex.Message}");
        }
    }

    // ---- the page script's questions (never leave this computer) ----

    private const int MaxInternalBody = 256 * 1024;

    private async Task HandleInternal(SessionEventArgs e)
    {
        var request = e.HttpClient.Request;
        var headers = new List<HttpHeader>
        {
            new("Access-Control-Allow-Origin", "*"),
            new("Cache-Control", "no-store"),
        };
        var path = Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : "";

        if (string.Equals(request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            headers.Add(new HttpHeader("Access-Control-Allow-Methods", "POST, OPTIONS"));
            headers.Add(new HttpHeader("Access-Control-Allow-Headers", "*"));
            headers.Add(new HttpHeader("Access-Control-Max-Age", "600"));
            e.GenericResponse(Array.Empty<byte>(), HttpStatusCode.NoContent, headers);
            return;
        }

        if (path == CosmeticBuilder.PingPath && string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            headers.Add(new HttpHeader("Content-Type", "application/json"));
            var ping = JsonSerializer.Serialize(new { ok = true, filters = _engine.LoadedVersion, protection = _settings.Current.ProtectionEnabled });
            e.Ok(Encoding.UTF8.GetBytes(ping), headers);
            return;
        }

        if (path != CosmeticBuilder.HiddenPath || !string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            e.GenericResponse(Array.Empty<byte>(), HttpStatusCode.NotFound, headers);
            return;
        }

        if (request.ContentLength > MaxInternalBody)
        {
            e.GenericResponse(Array.Empty<byte>(), HttpStatusCode.RequestEntityTooLarge, headers);
            return;
        }

        var answer = "[]";
        var engine = _engine.Current;
        if (engine is not null && _settings.Current.ProtectionEnabled && _settings.Current.CosmeticFiltering)
        {
            var body = await e.GetRequestBodyAsString();
            if (body.Length <= MaxInternalBody && CosmeticBuilder.ParseHiddenRequest(body) is { } parsed)
            {
                answer = engine.HiddenClassIdSelectorsJson(parsed.Classes, parsed.Ids, parsed.Exceptions);
            }
        }

        headers.Add(new HttpHeader("Content-Type", "application/json"));
        e.Ok(Encoding.UTF8.GetBytes(answer), headers);
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
            e.GenericResponse(Encoding.UTF8.GetBytes(PageText.PopupPage(url, settings.EffectiveLanguage)), HttpStatusCode.OK, new[]
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
                RespondBlocked(e, type, url, settings.EffectiveLanguage);
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
                RespondBlocked(e, type, url, settings.EffectiveLanguage);
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

    private static void RespondBlocked(SessionEventArgs e, string type, string url, string language)
    {
        var noStore = new HttpHeader("Cache-Control", "no-store");
        switch (type)
        {
            case "document":
                e.GenericResponse(Encoding.UTF8.GetBytes(PageText.BlockedPage(url, language)), HttpStatusCode.Forbidden, new[]
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
                var nonce = CspNonce.NewNonce();
                var payload = CosmeticBuilder.Build(engine.CosmeticResourcesJson(page.Url), nonce, CosmeticBuilder.EndpointFor(page.Url));
                if (!payload.IsEmpty)
                {
                    var body = await e.GetResponseBody();
                    if (body.Length > 0 && body.Length <= MaxHtmlBytes)
                    {
                        var connectOrigin = new Uri(CosmeticBuilder.EndpointFor(page.Url)).GetLeftPart(UriPartial.Authority);
                        // A policy can also sit in the page itself (<meta http-equiv="Content-Security-Policy">).
                        var changed = HtmlInjector.Inject(body, payload.Css, payload.Scripts, nonce,
                            policy => CspNonce.AllowConnect(CspNonce.AddNonce(policy, nonce), connectOrigin));
                        if (changed is not null)
                        {
                            e.SetResponseBody(changed);
                            AddNonceToCsp(response, nonce, connectOrigin);
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

    /// <summary>Adds our nonce (and our local address for connections) to every enforced Content-Security-Policy so what we insert can run.</summary>
    private static void AddNonceToCsp(Response response, string nonce, string connectOrigin)
    {
        const string name = "Content-Security-Policy";
        var headers = response.Headers.GetHeaders(name);
        if (headers is null || headers.Count == 0) return;

        var changed = headers.Select(h => CspNonce.AllowConnect(CspNonce.AddNonce(h.Value, nonce), connectOrigin)).ToList();
        response.Headers.RemoveHeader(name);
        foreach (var value in changed)
        {
            response.Headers.AddHeader(name, value);
        }
    }
}
