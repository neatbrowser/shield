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
        SummaryText.Text = Loc.T("stats.summary", s.Since.ToString("d", Loc.Culture), s.TotalBlockedEver.ToString("N0", Loc.Culture), s.Requests.ToString("N0", Loc.Culture),
            s.Blocked.ToString("N0", Loc.Culture), s.Redirected.ToString("N0", Loc.Culture), s.Rewritten.ToString("N0", Loc.Culture), s.PagesCleaned.ToString("N0", Loc.Culture), s.PopupsBlocked.ToString("N0", Loc.Culture));

        TopList.Items.Clear();
        foreach (var kv in s.TopHosts)
        {
            TopList.Items.Add($"{kv.Value.ToString("N0", Loc.Culture),8}   {kv.Key}");
        }

        RecentList.Items.Clear();
        foreach (var r in s.Recent.Take(100))
        {
            RecentList.Items.Add($"{r.Time:HH:mm:ss}   {r.Action,-8} {r.Type,-14} {r.Host}");
        }
    }
}
