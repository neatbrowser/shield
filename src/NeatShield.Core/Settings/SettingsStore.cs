using System.Text.Json;
using NeatShield.Core.Logging;

namespace NeatShield.Core.Settings;

/// <summary>
/// Holds the settings. <see cref="Current"/> is a snapshot that is never modified after it was
/// published: <see cref="Update"/> changes a copy and swaps it in, so readers on other threads
/// always see a consistent set of values.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _file;
    private readonly object _gate = new();
    private AppSettings _current;

    public SettingsStore(string file)
    {
        _file = file;
        _current = Load(file);
    }

    public event EventHandler? Changed;

    public AppSettings Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    public void Update(Action<AppSettings> change)
    {
        lock (_gate)
        {
            var next = _current.Clone();
            change(next);
            next.Normalize();
            Save(_file, next);
            _current = next;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static AppSettings Load(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file)) ?? new AppSettings();
                s.Normalize();
                return s;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"settings could not be read, using defaults: {ex.Message}");
        }

        var fresh = new AppSettings();
        fresh.Normalize();
        return fresh;
    }

    private static void Save(string file, AppSettings settings)
    {
        try
        {
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("settings could not be saved", ex);
        }
    }
}
