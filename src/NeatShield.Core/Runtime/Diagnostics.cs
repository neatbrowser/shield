using System.Runtime.InteropServices;
using System.Text;
using NeatShield.Core.Logging;

namespace NeatShield.Core.Runtime;

/// <summary>
/// A plain-text report the user can copy when something does not work. It holds no secrets: no certificate password,
/// no text of the user's own rules and no list of excluded sites (only how many there are).
/// </summary>
public static class Diagnostics
{
    public static string Report(ShieldRuntime rt, int logLines = 40)
    {
        var sb = new StringBuilder();
        void Line(string label, string? value) => sb.Append(label.PadRight(14)).Append(": ").AppendLine(value ?? "-");

        sb.AppendLine("NeatShield diagnostics");
        sb.AppendLine("======================");
        Line("Time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        Line("App", typeof(ShieldRuntime).Assembly.GetName().Version?.ToString());
        Line("System", $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), {RuntimeInformation.FrameworkDescription}");
        Line("Language", rt.Settings.Current.EffectiveLanguage + (string.IsNullOrEmpty(rt.Settings.Current.Language) ? " (automatic)" : ""));
        Line("Data folder", rt.Paths.Root);

        sb.AppendLine().AppendLine("Protection");
        Line("State", rt.State.ToString() + (rt.Error is null ? "" : " - " + rt.Error));
        Line("Proxy", rt.Proxy.State + (rt.Proxy.State == Proxy.ProxyState.Running ? $" on 127.0.0.1:{rt.Proxy.Port}" : "") + (rt.Proxy.LastError is null ? "" : " - " + rt.Proxy.LastError));
        Line("Windows proxy", rt.SystemProxy.Disabled ? "left alone (--no-system-proxy)" : OperatingSystem.IsWindows() ? (rt.SystemProxy.IsOurs() ? "set to NeatShield" : "not NeatShield's") : "n/a");

        sb.AppendLine().AppendLine("Filters");
        var best = rt.Filters.BestDir;
        Line("Release", best is null ? "none" : rt.Filters.ReadManifest(best)?.Version + "  (" + best + ")");
        Line("Engine", rt.Engine.Current is null ? "not loaded" : $"loaded {rt.Engine.LoadedVersion}, {(rt.Engine.LoadedFromCache ? "from cache" : "from lists")}, {rt.Engine.LastLoadTime.TotalSeconds:F1}s");
        var custom = rt.Settings.Current.CustomFilters;
        Line("Custom rules", string.IsNullOrWhiteSpace(custom) ? "none" : custom.Split('\n').Count(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('!')) + " lines");
        Line("Last update", rt.LastUpdateCheck?.ToString("yyyy-MM-dd HH:mm:ss") ?? "not checked in this run");

        sb.AppendLine().AppendLine("Certificate");
        try
        {
            var c = rt.Certificate.GetStatus();
            Line("Created", c.Exists ? "yes" : "no");
            Line("Trusted", c.Exists ? (c.Trusted ? "yes" : "NO") : "-");
            Line("Thumbprint", c.Thumbprint);
            Line("Expires", c.Expires?.ToString("yyyy-MM-dd"));
        }
        catch (Exception ex)
        {
            Line("Error", ex.Message);
        }

        var s = rt.Settings.Current;
        sb.AppendLine().AppendLine("Settings");
        Line("Port", s.Port.ToString());
        Line("HTTPS", s.FilterHttps ? "filtered" : "not filtered");
        Line("Element hiding", s.CosmeticFiltering ? "on" : "off");
        Line("Pop-ups", s.BlockPopups ? "blocked" : "allowed");
        Line("Auto update", s.AutoUpdate ? $"every {s.UpdateIntervalHours} h" : "off");
        Line("Excluded sites", s.ExcludedHosts.Count.ToString());

        var st = rt.Stats.Snapshot();
        sb.AppendLine().AppendLine("This run");
        Line("Requests", st.Requests.ToString());
        Line("Blocked", $"{st.Blocked} (pop-ups {st.PopupsBlocked}, replaced {st.Redirected}, parameters removed {st.Rewritten})");
        Line("Pages cleaned", st.PagesCleaned.ToString());

        sb.AppendLine().AppendLine($"Last {logLines} log lines");
        foreach (var l in Log.Tail(logLines)) sb.AppendLine(l);
        return sb.ToString();
    }
}
