using Noty.Core;

namespace Noty.Deck;

/// Resolved metrics for one fan.
///
/// Tabs *shingle*: each is full height but sits `Pitch` below the one before, so it
/// laps over it like a roof tile. That keeps every tab tall enough to carry a label
/// while the deck as a whole stays well short of the screen.
public sealed class DeckLayout
{
    public double ItemHeight;   // full height of one tab
    public double Pitch;        // top-to-top spacing; < ItemHeight means overlap
    public double MoreGap;
    public double MoreHeight;
    public int Count;
    public bool HasMore;
    public double PanelHeight;

    public double StackHeight => Count <= 0
        ? 0
        : (Count - 1) * Pitch + ItemHeight
          + (HasMore ? MoreGap + MoreHeight : 0)
          + DeckGeom.PlusGap + DeckGeom.PlusSize;

    public double Top => Math.Max(12, (PanelHeight - StackHeight) / 2);

    /// Centre of the strip of item `index` that is actually visible.
    public double Center(int index)
    {
        var strip = index == Count - 1 ? ItemHeight : Pitch;
        return Top + index * Pitch + strip / 2;
    }

    public double Cap => Math.Max(140, PanelHeight - 76);
    public bool Overflows => StackHeight > Cap;
}

public static class DeckGeom
{
    /// Every metric below is quoted at 100% and scaled by one preference, so the
    /// deck grows or shrinks without drifting out of proportion with itself.
    public static double Scale => Settings.DeckScale;

    // Rest — a 12 px pill of colour dashes
    public static double PillWidth => 12 * Scale;
    public static double DashHeight => 14 * Scale;
    public static double DashWidth => 7 * Scale;
    public static double DashGap => 5 * Scale;
    public static double PillPad => 7 * Scale;
    public const int MaxDashes = 14;

    // Fan
    public static double TabWidth => 30 * Scale;
    public static double TabGap => 7 * Scale;
    /// How far the next tab laps over the one before it.
    public static double TabLap => 40 * Scale;
    public static double PitchMin => 56 * Scale;
    public static double PitchMax => 106 * Scale;
    /// The strip is the label plus this much; the label is drawn inside it with
    /// LabelInset. Keeping the two different is what leaves the last glyph room —
    /// sizing the strip to exactly the text width truncates on rounding.
    public static double LabelPad => 20 * Scale;
    public static double LabelInset => 12 * Scale;
    /// Tabs and notes are drawn a little past the screen edge so their lean cannot
    /// open a wedge of background between them and the edge they are stuck to.
    public static double Bleed => 14 * Scale;

    /// Everything leans the same way — a deck of tabs at matching angles reads as
    /// deliberate, where per-note angles just look scattered.
    public const double LeanDegrees = 3.0;
    public static double Lean(bool onRight) => onRight ? -LeanDegrees : LeanDegrees;

    public static double ChipWidth => 30 * Scale;
    public static double ChipHeight => 24 * Scale;
    public static double ChipGap => 6 * Scale;
    public static double FanWidth => 50 * Scale;
    public static double PlusSize => 28 * Scale;
    public static double PlusGap => 12 * Scale;
    public static double MoreTabHeight => 34 * Scale;

    /// The deck may claim at most this much of the screen before tabs start shrinking.
    public const double HeightBudget = 0.68;

    /// The open note carries its own tab as a left gutter, so it reads as growing
    /// out of the deck rather than floating beside it.
    public static double GutterWidth => 30 * Scale;

    // Expanded — the note slides clear of the deck
    public const double EditorWidth = 460;
    public const double EditorHeight = 380;

    /// The open note runs to the screen edge and covers its own tab, exactly as a
    /// pulled sticky would. A little wider than the note so the lean has somewhere
    /// to go.
    public static double ExpandedWidth => Math.Max(FanWidth, EditorWidth) + 22;

    public static double PillHeight(int noteCount, double scale = 1)
    {
        var shown = Math.Min(noteCount, MaxDashes);
        var n = Math.Max(1, shown + (noteCount > MaxDashes ? 1 : 0));

        // WPF rounds every child and margin to a device pixel separately when
        // layout rounding is enabled. Round the same components here so the
        // explicit pill height cannot become shorter than its dash stack at a
        // fractional Windows display scale.
        return RoundToDevicePixels(PillPad, scale) * 2
             + n * RoundToDevicePixels(DashHeight, scale)
             + (n - 1) * RoundToDevicePixels(DashGap, scale);
    }

    private static double RoundToDevicePixels(double value, double scale)
    {
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            scale = 1;

        return Math.Round(value * scale) / scale;
    }

    /// `allowOverflow` is set once the whole deck has been asked for: the tabs then
    /// keep their full size and the deck scrolls under the wheel, rather than being
    /// squeezed until no label is readable.
    public static DeckLayout Layout(double panelHeight, int count, bool hasMore,
                                    DeckStyle style, double longestLabel = 0,
                                    bool allowOverflow = false)
    {
        var n = Math.Max(1, count);
        if (style == DeckStyle.Compact)
        {
            return new DeckLayout
            {
                ItemHeight = ChipHeight,
                Pitch = ChipHeight + ChipGap,
                MoreGap = ChipGap,
                MoreHeight = 22 * Scale,
                Count = n,
                HasMore = hasMore,
                PanelHeight = panelHeight,
            };
        }

        // The uncovered strip of each tab is sized to the longest label on the deck,
        // so titles read in full until they hit the cap and ellipsise.
        var pitch = Math.Min(PitchMax, Math.Max(PitchMin, longestLabel + LabelPad));

        // Guard rail: on a short display, shrink rather than run off-screen.
        var reserved = hasMore ? MoreTabHeight + TabGap : 0;
        var budget = panelHeight * HeightBudget - reserved;
        if (!allowOverflow && n * pitch + TabLap > budget)
            pitch = Math.Max(36 * Scale, (budget - TabLap) / n);

        return new DeckLayout
        {
            ItemHeight = pitch + TabLap,
            Pitch = pitch,
            MoreGap = TabGap,
            MoreHeight = MoreTabHeight,
            Count = n,
            HasMore = hasMore,
            PanelHeight = panelHeight,
        };
    }
}
