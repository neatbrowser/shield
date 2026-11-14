using System.Net;
using System.Security.Authentication;
using System.Text.Json;
using Neat.Adblock;
using NeatShield.Core.Proxy;

namespace NeatShield.Core.Runtime;

/// <summary>One line of the self-test. <see cref="Id"/> is stable (the window turns it into a text); <see cref="Detail"/> is for people who need to dig.</summary>
public sealed record SelfTestStep(string Id, bool Ok, string Detail);

/// <summary>
/// "Is protection really working on this computer?" Asks the same questions a browser would, without needing one:
/// is a filter list loaded, does the proxy answer, does an HTTPS connection through it open (this proves that Windows
/// trusts the NeatShield certificate), do the rules block a well-known ad address, and does Windows send traffic here.
/// Nothing leaves this computer: the only address contacted is NeatShield's own <c>neatshield.invalid</c>.
/// </summary>
public static class SelfTest
{
    private static readonly string[] KnownAds =
    {
        "https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js",
        "https://securepubads.g.doubleclick.net/tag/js/gpt.js",
        "https://googleads.g.doubleclick.net/pagead/ads",
    };

    public static async Task<IReadOnlyList<SelfTestStep>> RunAsync(ShieldRuntime runtime, CancellationToken ct = default)
    {
        var steps = new List<SelfTestStep>();

        var engine = runtime.Engine.Current;
        steps.Add(engine is null
            ? new SelfTestStep("filters", false, "No filters are loaded. Update the filters first.")
            : new SelfTestStep("filters", true, "Version " + (runtime.Engine.LoadedVersion ?? "?")));

        var running = runtime.State == ProtectionState.On && runtime.Proxy.State == ProxyState.Running;
        steps.Add(new SelfTestStep("proxy", running,
            running ? $"Listening on 127.0.0.1:{runtime.Proxy.Port}" : runtime.Error ?? "Protection is off."));
        if (!running)
        {
            return steps; // the rest cannot work without the proxy
        }

        steps.Add(await CheckHttpsAsync(runtime.Proxy.Port, ct));

        if (engine is not null)
        {
            // "Redirect" counts too: the filters swap some ad scripts for a harmless stand-in instead of blocking them.
            var handled = KnownAds.Count(u => engine.Check(u, "https://example.org/", "script", "GET").Action != AdblockAction.Allow);
            steps.Add(new SelfTestStep("rules", handled > 0, $"{handled} of {KnownAds.Length} well-known ad addresses are blocked or replaced"));
        }

        if (runtime.SystemProxy.Disabled)
        {
            steps.Add(new SelfTestStep("system", true, "The Windows proxy setting is left alone on purpose"));
        }
        else if (!OperatingSystem.IsWindows())
        {
            steps.Add(new SelfTestStep("system", true, "Not Windows: nothing to check"));
        }
        else
        {
            var ours = runtime.SystemProxy.IsOurs();
            steps.Add(new SelfTestStep("system", ours,
                ours ? "Windows sends web traffic to NeatShield" : "Windows is not set to use NeatShield (did another program change the proxy?)"));
        }

        return steps;
    }

    private static async Task<SelfTestStep> CheckHttpsAsync(int port, CancellationToken ct)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy("127.0.0.1", port),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(5),
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.GetAsync("https://" + CosmeticBuilder.InternalHost + CosmeticBuilder.PingPath, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return new SelfTestStep("https", false, $"The proxy answered {(int)response.StatusCode}");
            }

            using var json = JsonDocument.Parse(body);
            var ok = json.RootElement.TryGetProperty("ok", out var v) && v.ValueKind == JsonValueKind.True;
            return new SelfTestStep("https", ok, ok ? "An HTTPS connection through NeatShield opens and is trusted" : "Unexpected answer: " + body);
        }
        catch (Exception ex) when (HasInner<AuthenticationException>(ex))
        {
            return new SelfTestStep("https", false, "Windows does not trust the NeatShield certificate (install it from the Home or Settings page): " + ex.GetBaseException().Message);
        }
        catch (Exception ex)
        {
            return new SelfTestStep("https", false, ex.GetBaseException().Message);
        }
    }

    private static bool HasInner<T>(Exception ex) where T : Exception
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is T) return true;
        }

        return false;
    }
}
