using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Neat.Adblock;

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

    private void Try_Click(object sender, RoutedEventArgs e)
    {
        var rt = App.Runtime;
        var url = TryUrl.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            TryResult.Text = Loc.T("custom.try.badurl");
            return;
        }

        var engine = rt.Engine.Current;
        if (engine is null)
        {
            TryResult.Text = Loc.T("custom.try.noengine");
            return;
        }

        // The engine is rebuilt in the background after a save: do not answer from the old rules.
        if (rt.Engine.LoadedCustomRules != rt.Settings.Current.CustomFilters)
        {
            TryResult.Text = Loc.T("custom.try.wait");
            return;
        }

        var page = TryPage.Text.Trim();
        if (!Uri.TryCreate(page, UriKind.Absolute, out var pageUri) || pageUri.Scheme is not ("http" or "https"))
        {
            // Nothing given: pretend it is loaded by another website, so that third-party rules apply.
            page = uri.Host.Equals("example.org", StringComparison.OrdinalIgnoreCase) ? "https://example.net/" : "https://example.org/";
        }

        var type = (TryType.SelectedItem as ComboBoxItem)?.Content as string ?? "other";
        var result = engine.Check(url, page, type, "GET");
        TryResult.Text = result.Action switch
        {
            AdblockAction.Block => "\u26D4  " + Loc.T("custom.try.block"),
            AdblockAction.Redirect => "\u21AA  " + Loc.T("custom.try.redirect"),
            _ when result.RewrittenUrl is { } rewritten => "\u2713  " + Loc.T("custom.try.rewrite", rewritten),
            _ => "\u2713  " + Loc.T("custom.try.allow"),
        };
    }
}
