using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Noty.Core;

namespace Noty.Deck.Controls;

/// A peek at what is on a tab: a corner of the note's own paper, drawn out from
/// under the deck far enough to read its title, how far through its checklist it is
/// and its first few lines.
///
/// It is the note, not a tooltip about the note — same paper, same ink, same shape
/// as the sheet that opens when the tab is clicked, square where it meets the deck
/// and rounded where it leaves it.
///
/// Deliberately deaf to the mouse: the deck folds away when the pointer leaves the
/// strip along the edge, and this sits well outside that strip.
public sealed class PreviewCard : Border
{
    public const double CardWidth = 250;

    public PreviewCard(Note note, bool onRight)
    {
        var pal = note.Palette;

        // The extra bleed runs off the screen edge, exactly as a tab's does, so the
        // lean cannot open a wedge of background between the sheet and the edge it
        // is stuck to.
        Width = CardWidth + DeckGeom.Bleed;
        IsHitTestVisible = false;

        // Rounded where it leaves the deck, square where it meets the screen edge —
        // the same shape as a tab, and as the note that opens from one.
        CornerRadius = onRight
            ? new CornerRadius(12, 0, 0, 12)
            : new CornerRadius(0, 12, 12, 0);

        Background = new LinearGradientBrush(pal.Paper,
            Color.FromArgb(0xF2, pal.Paper.R, pal.Paper.G, pal.Paper.B), 90);
        BorderBrush = NoteColor.Tint(Colors.Black, 0.07);
        BorderThickness = new Thickness(0.5);

        // Quality, not the performance bias the tabs use. That one caches the effect
        // at reduced resolution, which on a blur this wide turned the rounded corners
        // into visible dark squares. One sheet is affordable to render properly.
        Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = 0.3,
            BlurRadius = 16,
            ShadowDepth = 5,
            Direction = onRight ? 200 : 340,
            RenderingBias = RenderingBias.Quality,
        };

        // Title and stamp take what they need; the body gets whatever the sheet has
        // left and is trimmed there, so a short tab shows a shorter peek rather than
        // a sheet that no longer matches its own edge.
        // The content is clipped, not the sheet: a rectangular clip on the sheet
        // itself would cut the corners off its own shadow.
        var grid = new Grid
        {
            ClipToBounds = true,
            Margin = onRight
                ? new Thickness(15, 11, 15 + DeckGeom.Bleed, 12)
                : new Thickness(15 + DeckGeom.Bleed, 11, 15, 12),
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = new TextBlock
        {
            Text = note.DisplayTitle,
            FontFamily = Ink.SystemFace,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = pal.InkAt(0.92),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        grid.Children.Add(title);

        var meta = new TextBlock
        {
            Text = Meta(note),
            FontFamily = Ink.SystemFace,
            FontSize = 10.5,
            Foreground = pal.InkAt(0.45),
            Margin = new Thickness(0, 3, 0, 0),
        };
        Grid.SetRow(meta, 1);
        grid.Children.Add(meta);

        var snippet = Snippet(note);
        if (snippet.Length > 0)
        {
            var body = new TextBlock
            {
                Text = snippet,
                FontFamily = Ink.BodyFamily,
                FontSize = Ink.BodySize(11.5),
                Foreground = pal.InkAt(0.72),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 7, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetRow(body, 2);
            grid.Children.Add(body);
        }

        Child = grid;
    }

    private static string Meta(Note note)
    {
        var parts = new List<string>();
        if (note.TaskProgress is { } p) parts.Add($"{p.Done}/{p.Total} done");
        parts.Add(Fmt.Ago(note.Modified));
        if (note.Pinned) parts.Add("pinned");
        return string.Join(" · ", parts);
    }

    /// The body after the line that became the title, since the title is already at
    /// the top of the card.
    private static string Snippet(Note note)
    {
        var lines = note.Body.Split('\n')
            .Skip(1)
            .Select(l => TaskSyntax.Stripped(l.TrimEnd('\r')).Trim())
            .Where(l => l.Length > 0)
            .Take(5)
            .ToList();
        var text = string.Join("\n", lines);
        return text.Length > 320 ? text[..320] + "…" : text;
    }
}
