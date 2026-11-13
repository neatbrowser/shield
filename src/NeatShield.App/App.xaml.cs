using Microsoft.UI.Xaml;
using NeatShield.Core.Logging;
using NeatShield.Core.Runtime;

namespace NeatShield.App;

public partial class App : Application
{
    private const string MutexName = @"Local\NeatShield.SingleInstance";
    private const string ShowEventName = @"Local\NeatShield.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private MainWindow? _window;
    private TrayController? _tray;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("unhandled: " + e.Exception);
            // Whatever happened, do not leave the user with a dead proxy setting.
            try { Runtime?.SystemProxy.Restore(); } catch (Exception) { /* nothing more to do */ }
        };
    }

    public static ShieldRuntime Runtime { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // One copy only: a second start just asks the first one to show its window.
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch (Exception) { /* first copy is closing */ }
            Environment.Exit(0);
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        var bundled = Path.Combine(AppContext.BaseDirectory, "filters");
        Runtime = new ShieldRuntime(bundledFilters: bundled);
        Loc.Language = Runtime.Settings.Current.EffectiveLanguage;
        _window = new MainWindow();
        _tray = new TrayController(_window, Runtime, ExitApp);
        _window.CloseToTray = () => !_exiting;

        var background = Environment.GetCommandLineArgs().Contains("--background", StringComparer.OrdinalIgnoreCase);
        if (!background && !Runtime.Settings.Current.StartMinimized)
        {
            _window.Activate();
        }

        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                _window.DispatcherQueue.TryEnqueue(_window.ShowAndFocus);
            }
        })
        { IsBackground = true, Name = "show-window-listener" };
        thread.Start();

        _ = Runtime.StartAsync();
    }

    private bool _exiting;

    private void ExitApp()
    {
        _exiting = true;
        try { Runtime.Shutdown(); } catch (Exception ex) { Log.Error("shutdown: " + ex); }
        _tray?.Dispose();
        Runtime.Dispose();
        Exit();
    }
}
