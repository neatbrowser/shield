using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using NeatShield.Core.Logging;

namespace NeatShield.Core.Proxy;

/// <summary>
/// Turns the Windows system proxy (the one Chrome, Edge and most apps follow) on and off.
/// The user's previous settings are saved to a file first, so they come back even after a crash:
/// the next start, or "NeatShield.Cli reset-proxy", puts them back. Settings that are not ours are never touched.
/// </summary>
public sealed class SystemProxy
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private readonly string _backupFile;

    public SystemProxy(string backupFile) => _backupFile = backupFile;

    /// <summary>
    /// When true the Windows setting is never read or written: the proxy only listens on its port and you point a
    /// program at it yourself (for example <c>chrome --proxy-server=127.0.0.1:8877</c>). Used by "run --no-system-proxy".
    /// </summary>
    public bool Disabled { get; set; }

    private sealed record Backup(int ProxyEnable, string? ProxyServer, string? ProxyOverride, string? AutoConfigUrl);

    public static string AddressFor(int port) => $"127.0.0.1:{port}";

    /// <summary>True when the system proxy is currently set to a local NeatShield port (any port).</summary>
    public bool IsOurs()
    {
        if (Disabled || !OperatingSystem.IsWindows()) return false;
        return ReadCurrent().server is { } s && LooksLikeOurs(s);
    }

    internal static bool LooksLikeOurs(string server) =>
        server.Split(';').Any(part =>
        {
            var v = part.Contains('=') ? part[(part.IndexOf('=') + 1)..] : part;
            return v.StartsWith("127.0.0.1:", StringComparison.Ordinal);
        });

    public void Enable(int port, IEnumerable<string> bypass)
    {
        if (Disabled)
        {
            Log.Info($"system proxy left alone (proxy listens on {AddressFor(port)})");
            return;
        }

        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The system proxy can only be set on Windows.");
        EnableWindows(port, bypass);
    }

    /// <summary>Puts the user's saved settings back. Does nothing if the proxy is no longer ours.</summary>
    public void Restore()
    {
        if (Disabled || !OperatingSystem.IsWindows()) return;
        RestoreWindows();
    }

    [SupportedOSPlatform("windows")]
    private void EnableWindows(int port, IEnumerable<string> bypass)
    {
        var current = ReadCurrent();
        // Save the user's settings once. If the proxy is already ours (after a crash), keep the older backup.
        if (!(current.server is { } s && LooksLikeOurs(s)) || !File.Exists(_backupFile))
        {
            var backup = new Backup(current.enable, current.server, current.over, current.pac);
            WriteAtomic(_backupFile, JsonSerializer.Serialize(backup));
        }

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
                        ?? throw new InvalidOperationException("Cannot open the Internet Settings registry key.");
        key.SetValue("ProxyServer", AddressFor(port), Microsoft.Win32.RegistryValueKind.String);
        key.SetValue("ProxyOverride", "<local>;" + string.Join(';', bypass), Microsoft.Win32.RegistryValueKind.String);
        key.SetValue("ProxyEnable", 1, Microsoft.Win32.RegistryValueKind.DWord);
        // A PAC script would take the lead over the manual proxy; it comes back with Restore().
        key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
        Notify();
        Log.Info($"system proxy set to {AddressFor(port)}");
    }

    [SupportedOSPlatform("windows")]
    private void RestoreWindows()
    {
        Backup? backup = null;
        try
        {
            if (File.Exists(_backupFile)) backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(_backupFile));
        }
        catch (Exception ex)
        {
            Log.Warn($"proxy backup unreadable: {ex.Message}");
        }

        var current = ReadCurrent();
        if (current.server is { } s && LooksLikeOurs(s))
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key is not null)
            {
                SetOrDelete(key, "ProxyServer", backup?.ProxyServer);
                SetOrDelete(key, "ProxyOverride", backup?.ProxyOverride);
                SetOrDelete(key, "AutoConfigURL", backup?.AutoConfigUrl);
                key.SetValue("ProxyEnable", backup?.ProxyEnable ?? 0, Microsoft.Win32.RegistryValueKind.DWord);
                Notify();
                Log.Info("system proxy restored");
            }
        }

        try { File.Delete(_backupFile); } catch (Exception) { /* ignore */ }
    }

    [SupportedOSPlatform("windows")]
    private static void SetOrDelete(Microsoft.Win32.RegistryKey key, string name, string? value)
    {
        if (value is null) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, value, Microsoft.Win32.RegistryValueKind.String);
    }

    [SupportedOSPlatform("windows")]
    private static (int enable, string? server, string? over, string? pac) ReadCurrent()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        if (key is null) return (0, null, null, null);
        return (key.GetValue("ProxyEnable") is int e ? e : 0,
            key.GetValue("ProxyServer") as string,
            key.GetValue("ProxyOverride") as string,
            key.GetValue("AutoConfigURL") as string);
    }

    private static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr handle, int option, IntPtr buffer, int length);

    /// <summary>Tells running programs the proxy changed (INTERNET_OPTION_SETTINGS_CHANGED, then REFRESH).</summary>
    private static void Notify()
    {
        try
        {
            InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
        }
        catch (Exception ex)
        {
            Log.Warn($"could not notify programs of the proxy change: {ex.Message}");
        }
    }
}
