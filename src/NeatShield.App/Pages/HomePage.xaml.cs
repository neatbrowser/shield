using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeatShield.Core.Runtime;
using Windows.UI;

namespace NeatShield.App.Pages;

public sealed partial class HomePage : Page
{
    private DispatcherTimer? _timer;
    private bool _updatingToggle;

    public HomePage() => InitializeComponent();

    private static ShieldRuntime Rt => App.Runtime;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Rt.Changed += OnChanged;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshStats();
        _timer.Start();
        Refresh();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        Rt.Changed -= OnChanged;
        _timer?.Stop();
    }

    private void OnChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Refresh);

    private void Refresh()
    {
        var state = Rt.State;
        var (color, title, sub) = state switch
        {
            ProtectionState.On => (Color.FromArgb(255, 0, 150, 112), Loc.T("home.on"),
                Loc.T("home.on.sub", Rt.Engine.LoadedVersion ?? Loc.T("home.nofilters.value"))),
            ProtectionState.Starting => (Color.FromArgb(255, 200, 150, 0), Loc.T("home.starting.title"), ""),
            ProtectionState.Error => (Color.FromArgb(255, 192, 57, 43), Loc.T("home.error"), Rt.Error ?? ""),
            _ => (Color.FromArgb(255, 140, 150, 160), Loc.T("home.off"), Loc.T("home.off.sub")),
        };

        Disc.Fill = new SolidColorBrush(color);
        StatusText.Text = title;
        SubText.Text = sub;
        Busy.IsActive = state == ProtectionState.Starting;
        ShieldGlyph.Visibility = state == ProtectionState.Starting ? Visibility.Collapsed : Visibility.Visible;

        _updatingToggle = true;
        Toggle.IsOn = state is ProtectionState.On or ProtectionState.Starting;
        Toggle.IsEnabled = state != ProtectionState.Starting;
        Toggle.OnContent = Loc.T("on");
        Toggle.OffContent = Loc.T("off");
        _updatingToggle = false;

        // Messages that need an action from the user.
        var cert = state == ProtectionState.Error && (Rt.Error?.Contains("certificate", StringComparison.OrdinalIgnoreCase) ?? false);
        if (cert)
        {
            Banner.Severity = InfoBarSeverity.Warning;
            Banner.Title = Loc.T("home.cert.title");
            Banner.Message = Loc.T("home.cert.msg");
            BannerButton.Visibility = Visibility.Visible;
            Banner.IsOpen = true;
        }
        else if (Rt.Engine.Current is null && !Rt.IsUpdating)
        {
            Banner.Severity = InfoBarSeverity.Informational;
            Banner.Title = Loc.T("home.nofilters.title");
            Banner.Message = Loc.T("home.nofilters.msg");
            BannerButton.Visibility = Visibility.Collapsed;
            Banner.IsOpen = true;
        }
        else
        {
            Banner.IsOpen = false;
        }

        RefreshStats();
    }

    private void RefreshStats()
    {
        var s = Rt.Stats.Snapshot();
        BlockedText.Text = (s.Blocked + s.Redirected).ToString("N0");
        TotalText.Text = s.TotalBlockedEver.ToString("N0");
        PagesText.Text = s.PagesCleaned.ToString("N0");
        RequestsText.Text = s.Requests.ToString("N0");
    }

    private async void Toggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingToggle) return;
        await Rt.SetProtectionAsync(Toggle.IsOn);
    }

    private async void BannerButton_Click(object sender, RoutedEventArgs e)
    {
        await Rt.SetProtectionAsync(true); // trusts the certificate (Windows asks), then starts
    }
}
