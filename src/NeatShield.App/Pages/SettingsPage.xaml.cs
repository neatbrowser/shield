using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeatShield.Core.Runtime;

namespace NeatShield.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage() => InitializeComponent();

    private static ShieldRuntime Rt => App.Runtime;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var s = Rt.Settings.Current;
        StartWithWindows.IsOn = s.StartWithWindows;
        StartMinimized.IsOn = s.StartMinimized;
        AutoUpdate.IsOn = s.AutoUpdate;
        Interval.Value = s.UpdateIntervalHours;
        Cosmetic.IsOn = s.CosmeticFiltering;
        FilterHttps.IsOn = s.FilterHttps;
        Excluded.Text = string.Join(Environment.NewLine, s.ExcludedHosts);
        Port.Value = s.Port;
        RefreshCert();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var hosts = Excluded.Text
            .Split(['\r', '\n', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(h => h.Trim())
            .ToList();
        var interval = double.IsNaN(Interval.Value) ? 6 : (int)Interval.Value;
        var port = double.IsNaN(Port.Value) ? 8877 : (int)Port.Value;

        Rt.Settings.Update(s =>
        {
            s.StartWithWindows = StartWithWindows.IsOn;
            s.StartMinimized = StartMinimized.IsOn;
            s.AutoUpdate = AutoUpdate.IsOn;
            s.UpdateIntervalHours = interval;
            s.CosmeticFiltering = Cosmetic.IsOn;
            s.FilterHttps = FilterHttps.IsOn;
            s.ExcludedHosts = hosts;
            s.Port = port;
        });

        StartupRegistration.Set(StartWithWindows.IsOn, Environment.ProcessPath ?? "");
        SavedText.Text = "Đã lưu lúc " + DateTime.Now.ToString("HH:mm:ss");
    }

    private void RefreshCert()
    {
        var c = Rt.Certificate.GetStatus();
        CertText.Text = !c.Exists
            ? "Chưa tạo chứng chỉ. Nó sẽ được tạo khi bạn bật bảo vệ lần đầu."
            : $"{(c.Trusted ? "Đã được Windows tin cậy" : "CHƯA được tin cậy")} · hết hạn {c.Expires:dd/MM/yyyy}";
    }

    private async void InstallCert_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.Run(() => Rt.Certificate.Trust()); // Windows shows its own confirmation
        }
        catch (Exception ex)
        {
            CertText.Text = "Không cài được: " + ex.Message;
            return;
        }

        RefreshCert();
    }

    private async void RemoveCert_Click(object sender, RoutedEventArgs e)
    {
        if (Rt.State == ProtectionState.On) await Rt.SetProtectionAsync(false);
        try
        {
            await Task.Run(() =>
            {
                Rt.Certificate.EnsureCreated();
                Rt.Certificate.Untrust();
            });
        }
        catch (Exception ex)
        {
            CertText.Text = "Không gỡ được: " + ex.Message;
            return;
        }

        RefreshCert();
    }

    private void OpenCertFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Rt.Certificate.EnsureCreated();
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Rt.Paths.CaCertFile}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CertText.Text = "Không mở được thư mục: " + ex.Message;
        }
    }

    private async void ResetProxy_Click(object sender, RoutedEventArgs e)
    {
        await Rt.SetProtectionAsync(false);
        Rt.SystemProxy.Restore();
        ResetText.Text = "Đã trả lại cài đặt proxy. Bảo vệ đang tắt.";
    }
}
