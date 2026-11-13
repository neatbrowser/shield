using System.Runtime.Versioning;
using NeatShield.Core.Logging;

namespace NeatShield.Core.Runtime;

/// <summary>Starts the app when the user signs in to Windows (HKCU Run key; no administrator rights needed).</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NeatShield";

    /// <param name="exePath">Full path of the app. Started with --background so it goes straight to the tray.</param>
    public static void Set(bool enabled, string exePath)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            SetWindows(enabled, exePath);
        }
        catch (Exception ex)
        {
            Log.Warn($"start-with-Windows could not be changed: {ex.Message}");
        }
    }

    public static bool IsSet() => OperatingSystem.IsWindows() && IsSetWindows();

    [SupportedOSPlatform("windows")]
    private static void SetWindows(bool enabled, string exePath)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) key.SetValue(ValueName, $"\"{exePath}\" --background");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsSetWindows()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }
}
