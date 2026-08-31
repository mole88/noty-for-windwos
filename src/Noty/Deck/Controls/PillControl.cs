using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Noty.Core;

namespace Noty.Deck.Controls;

/// At rest: a 12 px pill on the screen edge — one coloured dash per note.
public sealed class PillControl : Border
{
    public PillControl(IReadOnlyList<Note> notes)
    {
        var colours = new List<Brush>();
        if (notes.Count == 0)
        {
            colours.Add(NoteColor.Tint(Colors.Gray, 0.4));
        }
        else
        {
            foreach (var n in notes.Take(DeckGeom.MaxDashes)) colours.Add(n.Palette.DashBrush);
            if (notes.Count > DeckGeom.MaxDashes) colours.Add(NoteColor.Tint(Colors.Gray, 0.5));
        }

        // The gap goes *between* dashes only. Giving every dash half a gap top and
        // bottom made the pill one whole gap taller than DeckGeom.PillHeight says,
        // and the last dash was clipped off the end of it.
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        for (var i = 0; i < colours.Count; i++)
            stack.Children.Add(Dash(colours[i], i == colours.Count - 1 ? 0 : DeckGeom.DashGap));

        Child = stack;
        Width = DeckGeom.PillWidth;
        Height = DeckGeom.PillHeight(notes.Count);
        Padding = new Thickness(0, DeckGeom.PillPad, 0, DeckGeom.PillPad);
        CornerRadius = new CornerRadius(6);
        Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x1C, 0x1C, 0x1E));
        Effect = Shapes.Shadow(0.22, 6, -2, 1);
    }

    private static UIElement Dash(Brush color, double gapBelow) => new Border
    {
        Width = DeckGeom.DashWidth,
        Height = DeckGeom.DashHeight,
        CornerRadius = new CornerRadius(2.5),
        Background = color,
        Margin = new Thickness(0, 0, 0, gapBelow),
        HorizontalAlignment = HorizontalAlignment.Center,
    };
}
