using NeatShield.Core.Logging;

namespace NeatShield.Core.Filters;

/// <summary>
/// Downloaded filter releases: &lt;filters&gt;\releases\&lt;version&gt;\ (manifest.json plus the files it
/// lists) and &lt;filters&gt;\current.txt, which names the release in use. A release folder is only ever
/// written completely and then renamed into place, so a folder that exists is a whole release.
/// </summary>
public sealed class FilterStore
{
    private readonly AppPaths _paths;

    public FilterStore(AppPaths paths)
    {
        _paths = paths;
        Directory.CreateDirectory(ReleasesDir);
    }

    public string ReleasesDir => Path.Combine(_paths.FiltersDir, "releases");

    private string CurrentFile => Path.Combine(_paths.FiltersDir, "current.txt");

    /// <summary>The release the updater last installed, or null when none is usable.</summary>
    public string? CurrentDir
    {
        get
        {
            try
            {
                if (!File.Exists(CurrentFile)) return null;
                var name = File.ReadAllText(CurrentFile).Trim();
                if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || name.Contains("..")) return null;
                var dir = Path.Combine(ReleasesDir, name);
                return File.Exists(Path.Combine(dir, "manifest.json")) ? dir : null;
            }
            catch (Exception ex)
            {
                Log.Warn($"current filter release could not be read: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>The release shipped with the app, or null.</summary>
    public string? BundledDir =>
        File.Exists(Path.Combine(_paths.BundledFiltersDir, "manifest.json")) ? _paths.BundledFiltersDir : null;

    /// <summary>The release to use now: the downloaded one, else the bundled one.</summary>
    public string? BestDir => CurrentDir ?? BundledDir;

    public FilterManifest? ReadManifest(string releaseDir)
    {
        try
        {
            var m = FilterManifest.Parse(File.ReadAllBytes(Path.Combine(releaseDir, "manifest.json")));
            return m;
        }
        catch (Exception ex)
        {
            Log.Warn($"manifest of {releaseDir} could not be read: {ex.Message}");
            return null;
        }
    }

    public void SetCurrent(string releaseFolderName)
    {
        var tmp = CurrentFile + ".tmp";
        File.WriteAllText(tmp, releaseFolderName);
        File.Move(tmp, CurrentFile, overwrite: true);
    }

    /// <summary>Removes old releases and leftovers of interrupted downloads; keeps the newest <paramref name="keep"/> plus the current one.</summary>
    public void Prune(int keep = 2)
    {
        try
        {
            var current = CurrentDir;
            var dirs = new DirectoryInfo(ReleasesDir).GetDirectories();

            foreach (var d in dirs.Where(d => d.Name.StartsWith(".tmp-", StringComparison.Ordinal)))
            {
                TryDelete(d.FullName);
            }

            var releases = dirs
                .Where(d => !d.Name.StartsWith(".tmp-", StringComparison.Ordinal))
                .OrderByDescending(d => d.CreationTimeUtc)
                .ToList();
            foreach (var d in releases.Skip(keep))
            {
                if (string.Equals(d.FullName, current, StringComparison.OrdinalIgnoreCase)) continue;
                TryDelete(d.FullName);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"pruning old filter releases failed: {ex.Message}");
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"could not delete {dir}: {ex.Message}");
        }
    }
}
