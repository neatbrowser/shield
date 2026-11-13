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
            ProtectionState.On => (Color.FromArgb(255, 0, 150, 112), "Bảo vệ đang bật",
                $"Chặn quảng cáo và theo dõi cho trình duyệt và ứng dụng của bạn. Bộ lọc: {Rt.Engine.LoadedVersion ?? "chưa có"}."),
            ProtectionState.Starting => (Color.FromArgb(255, 200, 150, 0), "Đang bật bảo vệ…", ""),
            ProtectionState.Error => (Color.FromArgb(255, 192, 57, 43), "Không bật được bảo vệ", Rt.Error ?? ""),
            _ => (Color.FromArgb(255, 140, 150, 160), "Bảo vệ đang tắt", "Bật để chặn quảng cáo và theo dõi."),
        };

        Disc.Fill = new SolidColorBrush(color);
        StatusText.Text = title;
        SubText.Text = sub;
        Busy.IsActive = state == ProtectionState.Starting;
        ShieldGlyph.Visibility = state == ProtectionState.Starting ? Visibility.Collapsed : Visibility.Visible;

        _updatingToggle = true;
        Toggle.IsOn = state is ProtectionState.On or ProtectionState.Starting;
        Toggle.IsEnabled = state != ProtectionState.Starting;
        _updatingToggle = false;

        // Messages that need an action from the user.
        var cert = state == ProtectionState.Error && (Rt.Error?.Contains("certificate", StringComparison.OrdinalIgnoreCase) ?? false);
        if (cert)
        {
            Banner.Severity = InfoBarSeverity.Warning;
            Banner.Title = "Cần chứng chỉ NeatShield để lọc trang HTTPS";
            Banner.Message = "Windows sẽ hỏi bạn xác nhận khi cài chứng chỉ. Chứng chỉ được tạo trên máy này và khóa riêng không rời khỏi máy.";
            BannerButton.Visibility = Visibility.Visible;
            Banner.IsOpen = true;
        }
        else if (Rt.Engine.Current is null && !Rt.IsUpdating)
        {
            Banner.Severity = InfoBarSeverity.Informational;
            Banner.Title = "Chưa có bộ lọc";
            Banner.Message = "Mở trang Bộ lọc và bấm Cập nhật ngay.";
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
