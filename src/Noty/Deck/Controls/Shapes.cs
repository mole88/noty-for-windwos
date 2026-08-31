using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Noty.Deck.Controls;

public static class Shapes
{
    /// Rounded on the outward-facing side only, so the tab reads as docked to the
    /// edge it grew out of.
    public static Geometry EdgeTab(double w, double h, bool onRight, double r = 11)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            var size = new Size(r, r);
            if (onRight)
            {
                // rounded on the left (inward), square against the right screen edge
                c.BeginFigure(new Point(r, 0), true, true);
                c.LineTo(new Point(w, 0), true, false);
                c.LineTo(new Point(w, h), true, false);
                c.LineTo(new Point(r, h), true, false);
                c.ArcTo(new Point(0, h - r), size, 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(0, r), true, false);
                c.ArcTo(new Point(r, 0), size, 0, false, SweepDirection.Clockwise, true, false);
            }
            else
            {
                c.BeginFigure(new Point(0, 0), true, true);
                c.LineTo(new Point(w - r, 0), true, false);
                c.ArcTo(new Point(w, r), size, 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(w, h - r), true, false);
                c.ArcTo(new Point(w - r, h), size, 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(0, h), true, false);
            }
        }
        g.Freeze();
        return g;
    }

    /// The dashed rule the deck hangs from, right at the screen edge.
    public static System.Windows.Shapes.Line EdgeRule(double height, Brush stroke) => new()
    {
        X1 = 0, Y1 = 0, X2 = 0, Y2 = height,
        Stroke = stroke,
        StrokeThickness = 1,
        StrokeDashArray = new DoubleCollection { 3, 4 },
        IsHitTestVisible = false,
    };

    public static DropShadowEffect Shadow(double opacity, double radius, double dx, double dy) => new()
    {
        Color = Colors.Black,
        Opacity = opacity,
        BlurRadius = radius,
        ShadowDepth = Math.Sqrt(dx * dx + dy * dy),
        Direction = dx == 0 && dy == 0 ? 0 : Angle(dx, dy),
        // Every tab carries one of these and they all move at once during the reveal.
        // Quality re-renders the shadow on each frame of that; Performance caches it.
        RenderingBias = RenderingBias.Performance,
    };

    /// WPF measures shadow direction anticlockwise from east, with y growing down.
    private static double Angle(double dx, double dy)
    {
        var deg = Math.Atan2(-dy, dx) * 180 / Math.PI;
        return deg < 0 ? deg + 360 : deg;
    }
}
