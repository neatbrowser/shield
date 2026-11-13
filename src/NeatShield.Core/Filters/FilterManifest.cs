using System.Text.Json;
using System.Text.RegularExpressions;

namespace NeatShield.Core.Filters;

/// <summary>manifest.json of a neat-filters release.</summary>
public sealed class FilterManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex SafeName = new("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);
    private static readonly Regex SafeVersion = new("^[0-9A-Za-z][0-9A-Za-z._-]{0,63}$", RegexOptions.Compiled);

    public const int SupportedSchema = 1;

    public int Schema { get; set; }

    public string Version { get; set; } = "";

    public DateTimeOffset? Generated { get; set; }

    public List<FilterFile> Files { get; set; } = new();

    public static FilterManifest Parse(byte[] json)
    {
        var m = JsonSerializer.Deserialize<FilterManifest>(json, JsonOptions)
                ?? throw new InvalidDataException("manifest.json is empty");
        return m;
    }

    /// <summary>
    /// Checks what the updater relies on. File names and the version come from the network and end up
    /// in paths, so they are restricted to plain names here.
    /// </summary>
    public void Validate()
    {
        if (Schema != SupportedSchema)
        {
            throw new InvalidDataException($"manifest schema {Schema} is not supported (expected {SupportedSchema})");
        }

        if (!SafeVersion.IsMatch(Version) || Version.Contains(".."))
        {
            throw new InvalidDataException($"manifest version '{Version}' is not valid");
        }

        if (Files.Count == 0)
        {
            throw new InvalidDataException("manifest lists no files");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Files)
        {
            if (!SafeName.IsMatch(f.Name) || f.Name.Contains("..") ||
                f.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"manifest file name '{f.Name}' is not valid");
            }

            if (!seen.Add(f.Name))
            {
                throw new InvalidDataException($"manifest lists '{f.Name}' twice");
            }

            if (f.Sha256.Length != 64 || !f.Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException($"manifest entry '{f.Name}' has no valid sha256");
            }

            if (f.Size < 0)
            {
                throw new InvalidDataException($"manifest entry '{f.Name}' has a negative size");
            }
        }

        if (!Files.Any(f => f.Kind == "resources"))
        {
            throw new InvalidDataException("manifest lists no resources file");
        }

        if (!Files.Any(f => f.Kind == "list"))
        {
            throw new InvalidDataException("manifest lists no filter list");
        }
    }

    public int NetworkRules => Files.Sum(f => f.Rules?.Network ?? 0);

    public int CosmeticRules => Files.Sum(f => f.Rules?.Cosmetic ?? 0);
}

public sealed class FilterFile
{
    public string Name { get; set; } = "";

    /// <summary>"list" or "resources".</summary>
    public string Kind { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>"network", "popup" or "document" (lists only).</summary>
    public string Role { get; set; } = "network";

    /// <summary>"standard" or "strong".</summary>
    public string Tier { get; set; } = "standard";

    public bool Trusted { get; set; }

    public string Source { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public long Size { get; set; }

    public RuleCounts? Rules { get; set; }

    public int? InvalidLines { get; set; }
}

public sealed class RuleCounts
{
    public int Network { get; set; }

    public int Cosmetic { get; set; }
}
