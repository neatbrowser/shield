using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeatShield.Core.Filters;
using NeatShield.Core.Runtime;

namespace NeatShield.App.Pages;

public sealed partial class FiltersPage : Page
{
    public FiltersPage() => InitializeComponent();

    private static ShieldRuntime Rt => App.Runtime;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Rt.Changed += OnChanged;
        Rt.UpdateProgress += OnProgress;
        Refresh();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        Rt.Changed -= OnChanged;
        Rt.UpdateProgress -= OnProgress;
    }

    private void OnChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Refresh);

    private void OnProgress(object? sender, string message) => DispatcherQueue.TryEnqueue(() => ProgressText.Text = Loc.Progress(message));

    private void Refresh()
    {
        var dir = Rt.Engine.LoadedDir ?? Rt.Filters.BestDir;
        var manifest = dir is null ? null : Rt.Filters.ReadManifest(dir);
        VersionText.Text = manifest is null ? Loc.T("filters.none") : Loc.T("filters.version", manifest.Version);
        if (manifest is null)
        {
            RulesText.Text = "";
        }
        else
        {
            RulesText.Text = Loc.T("filters.rules", manifest.NetworkRules.ToString("N0"), manifest.CosmeticRules.ToString("N0"));
        }

        LastCheckText.Text = Rt.LastUpdateCheck is { } t ? Loc.T("filters.lastcheck", t.ToString("g", Loc.Culture)) : Loc.T("filters.nocheck");
        UpdateButton.IsEnabled = !Rt.IsUpdating;
        Ring.IsActive = Rt.IsUpdating;
        if (!Rt.IsUpdating) ProgressText.Text = "";

        if (Rt.LastUpdate is { } r && !Rt.IsUpdating)
        {
            ResultBar.IsOpen = true;
            switch (r.Status)
            {
                case UpdateStatus.Updated:
                    ResultBar.Severity = InfoBarSeverity.Success;
                    ResultBar.Title = Loc.T("filters.updated");
                    ResultBar.Message = Loc.T("filters.updated.msg", r.Version ?? "");
                    break;
                case UpdateStatus.UpToDate:
                    ResultBar.Severity = InfoBarSeverity.Informational;
                    ResultBar.Title = Loc.T("filters.uptodate");
                    ResultBar.Message = r.Version ?? "";
                    break;
                default:
                    ResultBar.Severity = InfoBarSeverity.Error;
                    ResultBar.Title = Loc.T("filters.failed");
                    ResultBar.Message = r.Error ?? "";
                    break;
            }
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        await Rt.UpdateFiltersAsync();
    }
}
