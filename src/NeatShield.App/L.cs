using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NeatShield.App;

/// <summary>
/// XAML helper: <c>loc:L.Key="home.title"</c> puts the text for that key into a TextBlock / Button / menu item,
/// <c>loc:L.Header="settings.port"</c> into the header of a toggle, box or number field.
/// </summary>
public static class L
{
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached("Key", typeof(string), typeof(L), new PropertyMetadata(null, (d, _) => Apply(d)));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.RegisterAttached("Header", typeof(string), typeof(L), new PropertyMetadata(null, (d, _) => Apply(d)));

    public static string? GetKey(DependencyObject d) => (string?)d.GetValue(KeyProperty);

    public static void SetKey(DependencyObject d, string? value) => d.SetValue(KeyProperty, value);

    public static string? GetHeader(DependencyObject d) => (string?)d.GetValue(HeaderProperty);

    public static void SetHeader(DependencyObject d, string? value) => d.SetValue(HeaderProperty, value);

    /// <summary>Sets the text of one element from its keys.</summary>
    public static void Apply(DependencyObject d)
    {
        if (GetKey(d) is { } key)
        {
            var text = Loc.T(key);
            switch (d)
            {
                case TextBlock tb: tb.Text = text; break;
                case ContentControl cc: cc.Content = text; break;
            }
        }

        if (GetHeader(d) is { } header)
        {
            var text = Loc.T(header);
            switch (d)
            {
                case ToggleSwitch ts: ts.Header = text; ts.OnContent = Loc.T("on"); ts.OffContent = Loc.T("off"); break;
                case NumberBox nb: nb.Header = text; break;
                case TextBox tx: tx.Header = text; break;
                case ComboBox cb: cb.Header = text; break;
            }
        }
    }

    /// <summary>Sets the texts again in a whole tree (after the language was changed).</summary>
    public static void ApplyTree(DependencyObject root)
    {
        Apply(root);
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            ApplyTree(VisualTreeHelper.GetChild(root, i));
        }
    }
}
