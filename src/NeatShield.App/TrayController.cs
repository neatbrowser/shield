using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NeatShield.Core.Logging;
using NeatShield.Core.Runtime;

namespace NeatShield.App;

/// <summary>The icon next to the clock: left click opens the window, right click shows a small menu.</summary>
public sealed class TrayController : IDisposable
{
    private readonly MainWindow _window;
    private readonly ShieldRuntime _runtime;
    private readonly TaskbarIcon _icon;
    private readonly MenuFlyoutItem _toggleItem;
    private System.Drawing.Icon? _on;
    private System.Drawing.Icon? _off;

    public TrayController(MainWindow window, ShieldRuntime runtime, Action exit)
    {
        _window = window;
        _runtime = runtime;

        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        _on = LoadIcon(Path.Combine(assets, "neatshield.ico"));
        _off = LoadIcon(Path.Combine(assets, "neatshield-off.ico"));

        var open = new MenuFlyoutItem { Text = "Mở NeatShield" };
        open.Click += (_, _) => _window.ShowAndFocus();

        _toggleItem = new MenuFlyoutItem { Text = "Tắt bảo vệ" };
        _toggleItem.Click += async (_, _) =>
        {
            try { await _runtime.SetProtectionAsync(_runtime.State != ProtectionState.On); }
            catch (Exception ex) { Log.Error("tray toggle: " + ex); }
        };

        var update = new MenuFlyoutItem { Text = "Cập nhật bộ lọc" };
        update.Click += async (_, _) =>
        {
            try { await _runtime.UpdateFiltersAsync(); }
            catch (Exception ex) { Log.Error("tray update: " + ex); }
        };

        var quit = new MenuFlyoutItem { Text = "Thoát NeatShield" };
        quit.Click += (_, _) => exit();

        var menu = new MenuFlyout();
        menu.Items.Add(open);
        menu.Items.Add(_toggleItem);
        menu.Items.Add(update);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(quit);

        var show = new XamlUICommand();
        show.ExecuteRequested += (_, _) => _window.ShowAndFocus();

        _icon = new TaskbarIcon
        {
            ToolTipText = "NeatShield",
            ContextFlyout = menu,
            LeftClickCommand = show,
            NoLeftClickDelay = true,
        };
        if (_off is not null) _icon.Icon = _off;
        _icon.ForceCreate();

        _runtime.Changed += (_, _) => _window.DispatcherQueue.TryEnqueue(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        var on = _runtime.State == ProtectionState.On;
        _icon.ToolTipText = on ? "NeatShield — đang bảo vệ" : "NeatShield — đã tắt";
        _toggleItem.Text = on ? "Tắt bảo vệ" : "Bật bảo vệ";
        var icon = on ? _on : _off;
        if (icon is not null) _icon.Icon = icon;
    }

    private static System.Drawing.Icon? LoadIcon(string path)
    {
        try { return new System.Drawing.Icon(path); }
        catch (Exception ex)
        {
            Log.Warn($"tray icon {path}: {ex.Message}");
            return null;
        }
    }

    public void Dispose() => _icon.Dispose();
}
