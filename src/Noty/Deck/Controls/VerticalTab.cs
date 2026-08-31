using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Noty.Core;

namespace Noty.Deck.Controls;

/// A tab keeps its colour and carries its label turned on its side.
///
/// Tabs overlap, so the label is pinned to the top of the tab — the part that stays
/// uncovered. Hovering lifts the tab a little to say it is live.
public sealed class VerticalTab : DeckButton
{
    private readonly Path _sheet;
    private readonly bool _onRight;
    private bool _hovering;

    public Note Note { get; }
    public bool IsOpen { get; }
    public bool Lifted { get; private set; }

    public VerticalTab(Note note, bool isOpen, double height, double strip, bool onRight)
    {
        Note = note;
        IsOpen = isOpen;
        _onRight = onRight;

        Width = DeckGeom.TabWidth + DeckGeom.Bleed;
        Height = height;

        _sheet = new Path
        {
            Data = Shapes.EdgeTab(Width, height, onRight),
            Fill = note.Palette.PaperBrush,
            Effect = Shapes.Shadow(isOpen ? 0.32 : 0.22, isOpen ? 9 : 6, onRight ? -3 : 3, 2),
        };
        Children.Add(_sheet);

        Children.Add(Label(note.DisplayTitle, strip, onRight, note.Palette.InkAt(0.85)));

        if (note.Pinned)
        {
            Children.Add(new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = note.Palette.DashBrush,
                HorizontalAlignment = onRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = onRight ? new Thickness(0, 7, DeckGeom.Bleed + 9, 0)
                                 : new Thickness(9, 7, 0, 0),
            });
        }

        // Everything leans the same way, anchored to the edge it is stuck to.
        RenderTransformOrigin = new Point(onRight ? 1 : 0, 0.5);
        RenderTransform = new RotateTransform(DeckGeom.Lean(onRight));

        ToolTip = note.DisplayTitle;
    }

    /// A label turned on its side. LayoutTransform (not RenderTransform) is what
    /// makes the rotated text *measure* rotated too — with a render transform the
    /// text still claims its unrotated width and bleeds across the note.
    internal static FrameworkElement Label(string title, double strip, bool onRight, Brush ink)
    {
        var text = new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontFamily = Ink.TabFamily,
            FontSize = Ink.TabFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = ink,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Width = Math.Max(20, strip - DeckGeom.LabelInset),
            Height = DeckGeom.TabWidth,
            LineHeight = DeckGeom.TabWidth,
            LayoutTransform = new RotateTransform(onRight ? 90 : -90),
        };

        return new Grid
        {
            Width = DeckGeom.TabWidth,
            Height = strip,
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = onRight ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Margin = onRight ? new Thickness(0, 0, DeckGeom.Bleed, 0)
                             : new Thickness(DeckGeom.Bleed, 0, 0, 0),
            Children = { text },
            IsHitTestVisible = false,
        };
    }

    protected override void OnHover(bool hovering)
    {
        _hovering = hovering;
        ApplyShadow();
    }

    /// A dragged tab is raised out of the shingle: a deeper shadow and a touch of
    /// scale, so it reads as picked up rather than slid.
    public void SetLifted(bool lifted)
    {
        if (Lifted == lifted) return;
        Lifted = lifted;
        ApplyShadow();
        var scale = EnsureScale();
        Animate(scale, ScaleTransform.ScaleXProperty, lifted ? 1.04 : 1, 220);
        Animate(scale, ScaleTransform.ScaleYProperty, lifted ? 1.04 : 1, 220);
    }

    private void ApplyShadow() => _sheet.Effect = Shapes.Shadow(
        Lifted ? 0.42 : (IsOpen || _hovering ? 0.32 : 0.22),
        Lifted ? 16 : (IsOpen || _hovering ? 9 : 6),
        _onRight ? -3 : 3, Lifted ? 6 : 2);
}
