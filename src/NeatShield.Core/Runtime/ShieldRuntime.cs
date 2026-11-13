using NeatShield.Core.Engine;
using NeatShield.Core.Filters;
using NeatShield.Core.Logging;
using NeatShield.Core.Proxy;
using NeatShield.Core.Security;
using NeatShield.Core.Settings;
using NeatShield.Core.Stats;
using Titanium.Web.Proxy;

namespace NeatShield.Core.Runtime;

public enum ProtectionState { Off, Starting, On, Error }

/// <summary>
/// Everything the app needs, put together: the one object the window and the tray talk to.
/// It also keeps the Windows proxy setting honest: whatever happens, the user's own setting comes back.
/// </summary>
public sealed class ShieldRuntime : IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _toggle = new(1, 1);
    private readonly SemaphoreSlim _update = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _background;
    private bool _disposed;

    public ShieldRuntime(string? dataRoot = null, string? bundledFilters = null)
    {
        Paths = new AppPaths(dataRoot, bundledFilters);
        Log.Init(Paths.LogDir);
        Settings = new SettingsStore(Paths.SettingsFile);
        Filters = new FilterStore(Paths);
        Engine = new EngineHost(Paths, Filters, () => Settings.Current.CustomFilters);
        Stats = new StatsCollector(Paths.StatsFile);
        SystemProxy = new SystemProxy(Paths.ProxyBackupFile);
        Proxy = new ProxyService(Settings, Engine, Stats);
        Certificate = new CertificateService(Paths, Settings, Proxy.CertificateManager);

        // Updates must not go through our own proxy.
        _http = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(20),
        })
        { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NeatShield/1.0");
        Updater = new FilterUpdater(_http, Filters);

        Settings.Changed += OnSettingsChanged;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public AppPaths Paths { get; }
    public SettingsStore Settings { get; }
    public FilterStore Filters { get; }
    public EngineHost Engine { get; }
    public StatsCollector Stats { get; }
    public SystemProxy SystemProxy { get; }
    public ProxyService Proxy { get; }
    public CertificateService Certificate { get; }
    public FilterUpdater Updater { get; }

    public ProtectionState State { get; private set; } = ProtectionState.Off;
    public string? Error { get; private set; }
    public DateTime? LastUpdateCheck { get; private set; }
    public UpdateResult? LastUpdate { get; private set; }
    public bool IsUpdating { get; private set; }

    /// <summary>Raised on any change of protection or update state. May come from a background thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Progress text while filters download.</summary>
    public event EventHandler<string>? UpdateProgress;

    /// <summary>Call once when the app starts.</summary>
    public async Task StartAsync()
    {
        // A crash may have left our proxy in the Windows settings: give the user's own settings back first.
        if (SystemProxy.IsOurs())
        {
            Log.Warn("found a leftover system proxy from an earlier run; restoring");
            SystemProxy.Restore();
        }

        await LoadBestEngineAsync();

        if (Settings.Current.ProtectionEnabled)
        {
            await SetProtectionAsync(true);
        }

        _background = Task.Run(() => BackgroundLoopAsync(_cts.Token));
    }

    // ---- protection on / off ----

    public async Task SetProtectionAsync(bool on)
    {
        await _toggle.WaitAsync();
        try
        {
            if (on) await Task.Run(EnableCore);
            else await Task.Run(DisableCore);
        }
        finally
        {
            _toggle.Release();
        }
    }

    private void EnableCore()
    {
        Set(ProtectionState.Starting, null);
        try
        {
            if (Engine.Current is null)
            {
                throw new InvalidOperationException("No filters are loaded yet. Update the filters first.");
            }

            Certificate.EnsureCreated();
            var s = Settings.Current;
            if (s.FilterHttps && !Certificate.GetStatus().Trusted)
            {
                Certificate.Trust(); // Windows asks the user to confirm
                if (!Certificate.GetStatus().Trusted)
                {
                    throw new InvalidOperationException(
                        "The NeatShield certificate is not trusted, so HTTPS pages cannot be filtered. Install it, or turn off HTTPS filtering in Settings.");
                }
            }

            Proxy.Start(Certificate.EnsureCreated);
            try
            {
                SystemProxy.Enable(Proxy.Port, MitmExclusionDefaults.SystemProxyBypassRules);
            }
            catch
            {
                Proxy.Stop();
                throw;
            }

            Settings.Update(x => x.ProtectionEnabled = true);
            Set(ProtectionState.On, null);
        }
        catch (Exception ex)
        {
            Log.Error($"could not turn protection on: {ex}");
            try { SystemProxy.Restore(); } catch (Exception) { /* already logged */ }
            try { Proxy.Stop(); } catch (Exception) { /* already logged */ }
            Set(ProtectionState.Error, ex.Message);
        }
    }

    private void DisableCore()
    {
        // The Windows setting first: with the proxy gone but the setting left, every site would stop loading.
        try { SystemProxy.Restore(); } catch (Exception ex) { Log.Error($"restore proxy: {ex.Message}"); }
        try { Proxy.Stop(); } catch (Exception ex) { Log.Error($"stop proxy: {ex.Message}"); }
        Settings.Update(x => x.ProtectionEnabled = false);
        Set(ProtectionState.Off, null);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // The user's own rules changed: build the engine again (in the background; the old one keeps working).
        if (!string.Equals(Engine.LoadedCustomRules, Settings.Current.CustomFilters, StringComparison.Ordinal) && Engine.LoadedDir is not null)
        {
            _ = Task.Run(async () =>
            {
                try { await Engine.ReloadAsync(); }
                catch (Exception ex) { Log.Error($"reload after custom rules change: {ex.Message}"); }
                Raise();
            });
        }

        // A new port needs a restart of the proxy while it is running (not while we are switching it ourselves).
        if (_toggle.CurrentCount == 1 && State == ProtectionState.On && Proxy.Port != Settings.Current.Port)
        {
            _ = Task.Run(async () =>
            {
                await SetProtectionAsync(false);
                Settings.Update(x => x.ProtectionEnabled = true);
                await SetProtectionAsync(true);
            });
        }
    }

    // ---- filters ----

    private async Task LoadBestEngineAsync()
    {
        var dir = Filters.BestDir;
        if (dir is null)
        {
            // First run without bundled filters: download.
            var r = await UpdateFiltersAsync();
            dir = r.Status == UpdateStatus.Failed ? null : Filters.BestDir;
        }

        if (dir is not null && Engine.Current is null)
        {
            try
            {
                await Engine.LoadAsync(dir);
            }
            catch (Exception ex)
            {
                Log.Error($"engine load failed: {ex}");
                Set(State, "The filters could not be loaded: " + ex.Message);
            }
        }
    }

    /// <summary>Checks neat-filters for a new release and, if there is one, downloads and loads it.</summary>
    public async Task<UpdateResult> UpdateFiltersAsync(CancellationToken ct = default)
    {
        await _update.WaitAsync(ct);
        try
        {
            IsUpdating = true;
            Raise();
            var progress = new Progress<string>(m => UpdateProgress?.Invoke(this, m));
            var result = await Updater.UpdateAsync(Settings.Current.FiltersBaseUrl, progress, ct);
            LastUpdate = result;
            LastUpdateCheck = DateTime.Now;

            if (result.Status == UpdateStatus.Updated && result.Dir is not null)
            {
                await Engine.LoadAsync(result.Dir, ct);
            }
            else if (result.Status == UpdateStatus.UpToDate && Engine.Current is null && Filters.BestDir is { } dir)
            {
                await Engine.LoadAsync(dir, ct);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"filter update failed: {ex}");
            var failed = new UpdateResult(UpdateStatus.Failed, null, null, ex.Message);
            LastUpdate = failed;
            LastUpdateCheck = DateTime.Now;
            return failed;
        }
        finally
        {
            IsUpdating = false;
            _update.Release();
            Raise();
        }
    }

    private async Task BackgroundLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct);
            var lastFlush = DateTime.UtcNow;
            while (!ct.IsCancellationRequested)
            {
                var s = Settings.Current;
                var due = LastUpdateCheck is null || DateTime.Now - LastUpdateCheck >= TimeSpan.FromHours(s.UpdateIntervalHours);
                if (s.AutoUpdate && due)
                {
                    await UpdateFiltersAsync(ct);
                }

                if (DateTime.UtcNow - lastFlush > TimeSpan.FromMinutes(1))
                {
                    Stats.Flush();
                    lastFlush = DateTime.UtcNow;
                }

                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // closing
        }
        catch (Exception ex)
        {
            Log.Error($"background loop: {ex}");
        }
    }

    // ---- shutdown ----

    private void OnProcessExit(object? sender, EventArgs e)
    {
        // Last chance, also when the app is closed from Task Manager's "End task" with a normal exit.
        try { SystemProxy.Restore(); } catch (Exception) { /* nothing more we can do */ }
    }

    /// <summary>Call when the app really closes (not when the window only goes to the tray).</summary>
    public void Shutdown()
    {
        if (_disposed) return;
        _cts.Cancel();
        try { SystemProxy.Restore(); } catch (Exception ex) { Log.Error($"restore proxy: {ex.Message}"); }
        try { Proxy.Stop(); } catch (Exception ex) { Log.Error($"stop proxy: {ex.Message}"); }
        Stats.Flush();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Shutdown();
        _disposed = true;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        Proxy.Dispose();
        Engine.Dispose();
        _http.Dispose();
        _cts.Dispose();
    }

    private void Set(ProtectionState state, string? error)
    {
        State = state;
        Error = error;
        Raise();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
