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

    private void OnProgress(object? sender, string message) => DispatcherQueue.TryEnqueue(() => ProgressText.Text = message);

    private void Refresh()
    {
        var dir = Rt.Engine.LoadedDir ?? Rt.Filters.BestDir;
        var manifest = dir is null ? null : Rt.Filters.ReadManifest(dir);
        VersionText.Text = manifest is null ? "Chưa có bộ lọc." : $"Phiên bản: {manifest.Version}";
        if (manifest is null)
        {
            RulesText.Text = "";
        }
        else
        {
            RulesText.Text = $"{manifest.NetworkRules:N0} quy tắc mạng · {manifest.CosmeticRules:N0} quy tắc ẩn phần tử";
        }

        LastCheckText.Text = Rt.LastUpdateCheck is { } t ? $"Kiểm tra lần cuối: {t:HH:mm dd/MM/yyyy}" : "Chưa kiểm tra trong lần chạy này.";
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
                    ResultBar.Title = "Đã cập nhật";
                    ResultBar.Message = $"Phiên bản {r.Version}";
                    break;
                case UpdateStatus.UpToDate:
                    ResultBar.Severity = InfoBarSeverity.Informational;
                    ResultBar.Title = "Đã là bản mới nhất";
                    ResultBar.Message = r.Version ?? "";
                    break;
                default:
                    ResultBar.Severity = InfoBarSeverity.Error;
                    ResultBar.Title = "Cập nhật thất bại";
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
