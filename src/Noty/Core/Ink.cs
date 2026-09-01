using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Noty.Core;

/// One entry per face offered for note bodies.
public sealed record NoteFace(string Name, string Family, double Bump);

public static class Ink
{
    /// Faces that suit a note, filtered to what is actually installed so the menu
    /// never offers something that would silently fall back.
    private static readonly NoteFace[] AllFaces =
    {
        new("System",        "",                  0),
        new("Segoe Script",  "Segoe Script",      1.5),
        new("Ink Free",      "Ink Free",          2.0),
        new("Comic Sans MS", "Comic Sans MS",     0.5),
        new("Gabriola",      "Gabriola",          3.0),
        new("Segoe UI",      "Segoe UI",          0),
        new("Georgia",       "Georgia",           0),
        new("Cambria",       "Cambria",           0),
        new("Consolas",      "Consolas",         -1),
    };

    private static IReadOnlyList<NoteFace>? _faces;

    public static IReadOnlyList<NoteFace> Faces => _faces ??= AllFaces
        .Where(f => f.Family.Length == 0 || Installed(f.Family))
        .ToList();

    public static NoteFace Face
    {
        get
        {
            var want = Settings.NoteFontName;
            return Faces.FirstOrDefault(f => f.Family == want) ?? Faces[0];
        }
    }

    public static FontFamily SystemFace { get; } = new("Segoe UI");

    public static FontFamily BodyFamily
    {
        get
        {
            var f = Face;
            return f.Family.Length == 0 ? SystemFace : new FontFamily(f.Family);
        }
    }

    public static double BodySize(double size) => size + Face.Bump;

    // Tab labels use the same face a shade bolder, so they hold up turned on their
    // side at this size.
    private const double BaseTabSize = 9.5;
    /// The label type grows with the deck, so a bigger deck is actually readable.
    public static double TabSize => BaseTabSize * Settings.DeckScale;
    public const double TabTracking = 0.1;

    public static FontFamily TabFamily => BodyFamily;
    public static double TabFontSize => TabSize + Face.Bump;

    /// Rendered width of a tab label, used to size the strip that shows it.
    /// Must measure with the same face the tab draws with or the strip will not fit.
    public static double MeasureTabLabel(string title)
    {
        var text = (title ?? "").ToUpperInvariant();
        if (text.Length == 0) return 0;
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TabFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            TabFontSize, Brushes.Black, 1.0);
        return ft.Width + TabTracking * text.Length;
    }

    private static HashSet<string>? _installed;

    private static bool Installed(string family)
    {
        _installed ??= Fonts.SystemFontFamilies
            .SelectMany(f => f.FamilyNames.Values)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _installed.Contains(family);
    }
}
