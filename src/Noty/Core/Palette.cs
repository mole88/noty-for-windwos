using System.Windows.Media;

namespace Noty.Core;

/// Slightly deeper than a highlighter pastel, so a note reads as paper with colour
/// in it rather than a tinted white rectangle. Same eight colours as the original.
public sealed class NoteColor
{
    public string Name { get; }
    public Color Paper { get; }   // note body background
    public Color Dash { get; }    // saturated edge dash / colour bar
    public Color Ink { get; }     // text colour on paper

    public SolidColorBrush PaperBrush { get; }
    public SolidColorBrush DashBrush { get; }
    public SolidColorBrush InkBrush { get; }

    private NoteColor(string name, uint paper, uint dash, uint ink)
    {
        Name = name;
        Paper = Hex(paper);
        Dash = Hex(dash);
        Ink = Hex(ink);
        PaperBrush = Freeze(Paper);
        DashBrush = Freeze(Dash);
        InkBrush = Freeze(Ink);
    }

    public static readonly IReadOnlyList<NoteColor> All = new[]
    {
        new NoteColor("Lemon", 0xFCE795, 0xE0AD08, 0x3A3008),
        new NoteColor("Peach", 0xFBCFA6, 0xE2762A, 0x422413),
        new NoteColor("Rose",  0xFAC4D1, 0xDC4570, 0x40161F),
        new NoteColor("Lilac", 0xD9C7FA, 0x7C4DEE, 0x2A1B44),
        new NoteColor("Sky",   0xBEDDFA, 0x2280D6, 0x13293A),
        new NoteColor("Mint",  0xB4E8D0, 0x0E9B6E, 0x0F2E23),
        new NoteColor("Sand",  0xE3D3B4, 0xA37B3C, 0x372C18),
        new NoteColor("Slate", 0xCBD6E2, 0x4E6579, 0x1A242E),
    };

    public static NoteColor At(int i) => All[((i % All.Count) + All.Count) % All.Count];

    /// Ink at a given opacity — used all over for secondary text on paper.
    public Brush InkAt(double alpha) => Tint(Ink, alpha);
    public Brush DashAt(double alpha) => Tint(Dash, alpha);

    public static Brush Tint(Color c, double alpha)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Color Hex(uint v) => Color.FromRgb(
        (byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
}
