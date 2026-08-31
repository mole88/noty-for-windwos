using System.Windows;

namespace Noty.Windows;

/// The dark control set for Noty's ordinary windows.
///
/// Merged per window rather than into the application, so it reaches All Notes and
/// Settings — which are built from stock WPF controls — without touching the deck,
/// which draws itself and wants nothing from a control template.
internal static class Theme
{
    private static readonly Uri Source =
        new("/Noty;component/Themes/Windows.xaml", UriKind.Relative);

    public static void Apply(Window window) =>
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = Source });
}
