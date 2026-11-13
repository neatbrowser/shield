using System.Security.Cryptography;
using Neat.Adblock;
using NeatShield.Core.Filters;
using NeatShield.Core.Logging;

namespace NeatShield.Core.Engine;

/// <summary>
/// Owns the engine in use. A new release is built on a background thread and swapped in; requests
/// that are being checked keep the old engine alive until they finish.
/// </summary>
public sealed class EngineHost : IDisposable
{
    private readonly AppPaths _paths;
    private readonly FilterStore _store;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private AdblockEngine? _engine;

    public EngineHost(AppPaths paths, FilterStore store)
    {
        _paths = paths;
        _store = store;
    }

    /// <summary>Engine in use, or null before the first load.</summary>
    public AdblockEngine? Current => Volatile.Read(ref _engine);

    public string? LoadedVersion { get; private set; }

    public string? LoadedDir { get; private set; }

    public bool LoadedFromCache { get; private set; }

    public TimeSpan LastLoadTime { get; private set; }

    public event EventHandler? EngineChanged;

    public async Task LoadAsync(string releaseDir, CancellationToken ct = default)
    {
        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(releaseDir, "manifest.json"), ct).ConfigureAwait(false);
            var key = Convert.ToHexString(SHA256.HashData(manifestBytes));
            var cachePath = Path.Combine(_paths.CacheDir, "engine.cache");

            var started = DateTime.UtcNow;
            var fromCache = false;
            var engine = await Task.Run(() => AdblockEngine.Load(releaseDir, cachePath, key, out fromCache), ct).ConfigureAwait(false);
            LastLoadTime = DateTime.UtcNow - started;

            var version = _store.ReadManifest(releaseDir)?.Version;
            var old = Interlocked.Exchange(ref _engine, engine);
            LoadedVersion = version;
            LoadedDir = releaseDir;
            LoadedFromCache = fromCache;
            old?.Dispose();

            Log.Info($"engine loaded from {releaseDir} (version {version}, {(fromCache ? "cache" : "lists")}, {LastLoadTime.TotalSeconds:F1}s)");
            EngineChanged?.Invoke(this, EventArgs.Empty);

            if (!fromCache)
            {
                // Next start can then skip parsing the lists. Failure only costs start-up time.
                _ = Task.Run(() =>
                {
                    try
                    {
                        engine.Save(cachePath, key);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"engine cache not written: {ex.Message}");
                    }
                });
            }
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _engine, null)?.Dispose();
        _loadGate.Dispose();
    }
}
