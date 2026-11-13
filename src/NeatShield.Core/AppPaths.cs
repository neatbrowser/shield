namespace NeatShield.Core;

/// <summary>Where NeatShield keeps its files.</summary>
public sealed class AppPaths
{
    /// <param name="root">Data folder. Default: %LOCALAPPDATA%\NeatShield.</param>
    /// <param name="bundledFilters">Folder with a neat-filters release shipped next to the app (first-run / offline fallback).</param>
    public AppPaths(string? root = null, string? bundledFilters = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeatShield");
        BundledFiltersDir = bundledFilters ?? Path.Combine(AppContext.BaseDirectory, "filters");
        foreach (var dir in new[] { Root, FiltersDir, CacheDir, CertDir, LogDir })
        {
            Directory.CreateDirectory(dir);
        }
    }

    public string Root { get; }

    /// <summary>Downloaded releases: filters\releases\&lt;version&gt;\ and filters\current.txt.</summary>
    public string FiltersDir => Path.Combine(Root, "filters");

    public string CacheDir => Path.Combine(Root, "cache");

    public string CertDir => Path.Combine(Root, "cert");

    public string LogDir => Path.Combine(Root, "logs");

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string StatsFile => Path.Combine(Root, "stats.json");

    /// <summary>The user's own proxy settings, saved while NeatShield has the system proxy.</summary>
    public string ProxyBackupFile => Path.Combine(Root, "proxy-backup.json");

    /// <summary>Public part of the root CA, for browsers that keep their own certificate list (Firefox).</summary>
    public string CaCertFile => Path.Combine(CertDir, "NeatShield-Root-CA.cer");

    public string CaKeyFile => Path.Combine(CertDir, "neatshield-ca.pfx");

    public string BundledFiltersDir { get; }
}
