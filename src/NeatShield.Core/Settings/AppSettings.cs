using System.Text.Json;

namespace NeatShield.Core.Settings;

public sealed class AppSettings
{
    public const string DefaultFiltersBaseUrl = "https://github.com/neatbrowser/neat-filters/releases/latest/download/";

    /// <summary>Port of the local proxy (127.0.0.1 only).</summary>
    public int Port { get; set; } = 8877;

    /// <summary>Decrypt HTTPS (needs the local root certificate) so that requests inside it can be filtered.</summary>
    public bool FilterHttps { get; set; } = true;

    /// <summary>Hide page elements and inject scriptlets into HTML pages.</summary>
    public bool CosmeticFiltering { get; set; } = true;

    /// <summary>Block pop-up windows that the filters' $popup rules name.</summary>
    public bool BlockPopups { get; set; } = true;

    public bool AutoUpdate { get; set; } = true;

    public int UpdateIntervalHours { get; set; } = 6;

    public bool StartWithWindows { get; set; }

    public bool StartMinimized { get; set; }

    /// <summary>Protection was on when the app last ran; it is switched on again at the next start.</summary>
    public bool ProtectionEnabled { get; set; }

    public string FiltersBaseUrl { get; set; } = DefaultFiltersBaseUrl;

    /// <summary>Hosts that are never decrypted or filtered. "example.com" also covers its subdomains; "*.example.com" is the same.</summary>
    public List<string> ExcludedHosts { get; set; } = new();

    /// <summary>Password of the root CA's key file, protected with DPAPI.</summary>
    public string? CaSecret { get; set; }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this)) ?? new AppSettings();

    /// <summary>Puts every value back in its allowed range.</summary>
    public void Normalize()
    {
        if (Port is < 1024 or > 65535) Port = 8877;
        if (UpdateIntervalHours is < 1 or > 168) UpdateIntervalHours = 6;
        if (!Uri.TryCreate(FiltersBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            FiltersBaseUrl = DefaultFiltersBaseUrl;
        }
        else if (!FiltersBaseUrl.EndsWith('/'))
        {
            FiltersBaseUrl += "/";
        }

        ExcludedHosts = (ExcludedHosts ?? new List<string>())
            .Select(h => h.Trim().ToLowerInvariant())
            .Where(h => h.Length > 0)
            .Distinct()
            .ToList();
    }
}
