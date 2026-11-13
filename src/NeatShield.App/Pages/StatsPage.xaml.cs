using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NeatShield.App.Pages;

public sealed partial class StatsPage : Page
{
    private DispatcherTimer? _timer;

    public StatsPage() => InitializeComponent();

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e) => _timer?.Stop();

    private void Refresh()
    {
        var s = App.Runtime.Stats.Snapshot();
        SummaryText.Text =
            $"Từ {s.Since:dd/MM/yyyy}: {s.TotalBlockedEver:N0} yêu cầu đã bị chặn. " +
            $"Lần chạy này: {s.Requests:N0} yêu cầu, {s.Blocked:N0} bị chặn, {s.Redirected:N0} được thay thế an toàn, " +
            $"{s.Rewritten:N0} bỏ tham số theo dõi, {s.PagesCleaned:N0} trang đã ẩn quảng cáo, {s.PopupsBlocked:N0} popup bị chặn.";

        TopList.Items.Clear();
        foreach (var kv in s.TopHosts)
        {
            TopList.Items.Add($"{kv.Value,8:N0}   {kv.Key}");
        }

        RecentList.Items.Clear();
        foreach (var r in s.Recent.Take(100))
        {
            RecentList.Items.Add($"{r.Time:HH:mm:ss}   {r.Action,-8} {r.Type,-14} {r.Host}");
        }
    }
}
