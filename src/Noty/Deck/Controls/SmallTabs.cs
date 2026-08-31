using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Noty.Core;

namespace Noty.Deck.Controls;

/// Compact style — colour only, so the deck barely touches the screen.
public sealed class ChipTab : DeckButton
{
    public Note Note { get; }

    public ChipTab(Note note, bool isOpen, bool onRight)
    {
        Note = note;
        Width = DeckGeom.ChipWidth + DeckGeom.Bleed / 2;
        Height = DeckGeom.ChipHeight;
        Children.Add(new Path
        {
            Data = Shapes.EdgeTab(Width, Height, onRight, 7),
            Fill = note.Palette.DashBrush,
            Effect = Shapes.Shadow(isOpen ? 0.34 : 0.22, isOpen ? 8 : 5, onRight ? -2 : 2, 1),
        });
        RenderTransformOrigin = new Point(onRight ? 1 : 0, 0.5);
        RenderTransform = new RotateTransform(DeckGeom.Lean(onRight) * 0.6);
        ToolTip = note.DisplayTitle;
    }
}

/// The rest of the deck, behind one tab.
public sealed class MoreTab : DeckButton
{
    public MoreTab(int count, double height, bool onRight)
    {
        Width = DeckGeom.TabWidth;
        Height = height;
        Children.Add(new Path
        {
            Data = Shapes.EdgeTab(Width, height, onRight, 9),
            Fill = new SolidColorBrush(Color.FromArgb(0xD8, 0x2A, 0x2A, 0x2E)),
            Effect = Shapes.Shadow(0.18, 5, onRight ? -2 : 2, 1),
        });
        Children.Add(new TextBlock
        {
            Text = $"+{count}",
            FontFamily = Ink.SystemFace,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = NoteColor.Tint(Colors.White, 0.72),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        });
        ToolTip = $"{count} more note{(count == 1 ? "" : "s")}";
    }
}

/// An empty deck still draws one tab, so the stack is never zero-height — and it
/// doubles as the way to make the first note.
public sealed class EmptyTab : DeckButton
{
    public EmptyTab(double height, double strip, bool onRight)
    {
        Width = DeckGeom.TabWidth;
        Height = height;
        Children.Add(new Path
        {
            Data = Shapes.EdgeTab(Width, height, onRight),
            Fill = new SolidColorBrush(Color.FromArgb(0xD0, 0x2A, 0x2A, 0x2E)),
            Effect = Shapes.Shadow(0.18, 6, onRight ? -2 : 2, 1),
        });
        var label = VerticalTab.Label("New note", strip, onRight, NoteColor.Tint(Colors.White, 0.72));
        label.Margin = new Thickness(0);
        Children.Add(label);
        ToolTip = "New note";
    }
}

/// The + in the bottom corner while the deck is open.
public sealed class PlusButton : DeckButton
{
    private readonly Ellipse _disc;

    public PlusButton()
    {
        Width = DeckGeom.PlusSize;
        Height = DeckGeom.PlusSize;
        _disc = new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromArgb(0xE6, 0x33, 0x33, 0x38)),
            Effect = Shapes.Shadow(0.22, 5, 0, 1),
        };
        Children.Add(_disc);
        Children.Add(new TextBlock
        {
            Text = "+",
            FontFamily = Ink.SystemFace,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = NoteColor.Tint(Colors.White, 0.85),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -2, 0, 0),
            IsHitTestVisible = false,
        });
        RenderTransformOrigin = new Point(0.5, 0.5);
        ToolTip = "New note  " + Settings.ScNewNote;
    }

    protected override void OnHover(bool hovering)
    {
        var scale = EnsureScale();
        Animate(scale, ScaleTransform.ScaleXProperty, hovering ? 1.08 : 1, 150);
        Animate(scale, ScaleTransform.ScaleYProperty, hovering ? 1.08 : 1, 150);
    }
}
