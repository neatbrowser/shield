using System.Collections.Concurrent;
using System.Text.Json;
using NeatShield.Core.Logging;

namespace NeatShield.Core.Stats;

public sealed record BlockedEntry(DateTime Time, string Host, string Type, string Action);

public sealed record StatsSnapshot(
    long Requests,
    long Blocked,
    long Redirected,
    long Rewritten,
    long PagesCleaned,
    long TotalBlockedEver,
    DateTime Since,
    IReadOnlyList<KeyValuePair<string, long>> TopHosts,
    IReadOnlyList<BlockedEntry> Recent);

/// <summary>Thread-safe counters for this run, plus a lifetime total that is saved to disk.</summary>
public sealed class StatsCollector
{
    private const int MaxHosts = 5000;
    private const int MaxRecent = 100;

    private readonly string _file;
    private readonly ConcurrentDictionary<string, long> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<BlockedEntry> _recent = new();
    private long _requests;
    private long _blocked;
    private long _redirected;
    private long _rewritten;
    private long _pagesCleaned;
    private long _lifetimeBlocked;
    private DateTime _since = DateTime.Now;

    public StatsCollector(string file)
    {
        _file = file;
        try
        {
            if (File.Exists(file))
            {
                var saved = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(file));
                if (saved is not null)
                {
                    _lifetimeBlocked = saved.TotalBlocked;
                    _since = saved.Since;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"stats could not be read: {ex.Message}");
        }
    }

    public void OnRequest() => Interlocked.Increment(ref _requests);

    public void OnBlocked(string host, string type)
    {
        Interlocked.Increment(ref _blocked);
        Interlocked.Increment(ref _lifetimeBlocked);
        Note(host, type, "block");
    }

    public void OnRedirected(string host, string type)
    {
        Interlocked.Increment(ref _redirected);
        Interlocked.Increment(ref _lifetimeBlocked);
        Note(host, type, "redirect");
    }

    public void OnRewritten() => Interlocked.Increment(ref _rewritten);

    public void OnPageCleaned() => Interlocked.Increment(ref _pagesCleaned);

    private void Note(string host, string type, string action)
    {
        if (_hosts.Count < MaxHosts || _hosts.ContainsKey(host))
        {
            _hosts.AddOrUpdate(host, 1, (_, n) => n + 1);
        }

        _recent.Enqueue(new BlockedEntry(DateTime.Now, host, type, action));
        while (_recent.Count > MaxRecent && _recent.TryDequeue(out _))
        {
        }
    }

    public StatsSnapshot Snapshot() => new(
        Interlocked.Read(ref _requests),
        Interlocked.Read(ref _blocked),
        Interlocked.Read(ref _redirected),
        Interlocked.Read(ref _rewritten),
        Interlocked.Read(ref _pagesCleaned),
        Interlocked.Read(ref _lifetimeBlocked),
        _since,
        _hosts.OrderByDescending(kv => kv.Value).Take(10).ToList(),
        _recent.Reverse().ToList());

    /// <summary>Writes the lifetime total. Called from time to time and when the app closes.</summary>
    public void Flush()
    {
        try
        {
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new Persisted(Interlocked.Read(ref _lifetimeBlocked), _since)));
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"stats could not be saved: {ex.Message}");
        }
    }

    private sealed record Persisted(long TotalBlocked, DateTime Since);
}
