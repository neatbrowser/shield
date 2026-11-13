using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NeatShield.App.Pages;

/// <summary>The user's own rules. Saving rebuilds the engine in the background; the old one keeps working meanwhile.</summary>
public sealed partial class CustomFiltersPage : Page
{
    public CustomFiltersPage() => InitializeComponent();

    private void Page_Loaded(object sender, RoutedEventArgs e) =>
        Rules.Text = App.Runtime.Settings.Current.CustomFilters.Replace("\n", "\r");

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // A TextBox keeps lines apart with \r; the settings store uses \n.
        var text = Rules.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        App.Runtime.Settings.Update(s => s.CustomFilters = text);

        var lines = text.Split('\n').Count(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('!'));
        StatusText.Text = Loc.T("custom.saved", DateTime.Now.ToString("HH:mm:ss"), lines);
    }
}
