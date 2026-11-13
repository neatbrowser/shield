using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeatShield.App.Pages;
using WinRT.Interop;

namespace NeatShield.App;

public sealed partial class MainWindow : Window
{
    /// <summary>Returns true while closing the window should only hide it (the app keeps running in the tray).</summary>
    public Func<bool> CloseToTray { get; set; } = () => true;

    public MainWindow()
    {
        InitializeComponent();
        Title = "NeatShield";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 700));
        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "neatshield.ico"));
        }
        catch (Exception)
        {
            // the icon is cosmetic
        }

        AppWindow.Closing += OnClosing;
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (CloseToTray())
        {
            args.Cancel = true;
            sender.Hide();
        }
    }

    public void ShowAndFocus()
    {
        AppWindow.Show();
        Activate();
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag) return;
        var page = tag switch
        {
            "filters" => typeof(FiltersPage),
            "stats" => typeof(StatsPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage),
        };
        ContentFrame.Navigate(page);
    }
}
