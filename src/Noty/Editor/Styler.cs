using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Noty.Core;
using Noty.Interop;

namespace Noty.Editor;

/// Renders the plain-text body into a FlowDocument: ticked-off tasks struck through
/// and dimmed, and inline Markdown styled in place.
///
/// The source stays plain text. Markdown punctuation is kept in the document so
/// editing and export still see it, but its runs are made transparent and nearly
/// zero-width in the rendered note.
public static class Styler
{
    private const double HiddenMarkerSize = 0.1;

    private readonly record struct CharStyle(
        bool Bold, bool Italic, bool Code, bool Strike, bool Dim);

    /// One face that carries both ☐ and ☑, so the two boxes match each other.
    private static readonly FontFamily MarkerFace =
        new("Segoe UI Symbol, Segoe UI, Arial Unicode MS");

    public static FlowDocument Build(string text, NoteColor palette, double size)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(15, 6, 15, 10),
            FontFamily = Ink.BodyFamily,
            FontSize = Ink.BodySize(size),
            Foreground = palette.InkBrush,
            LineHeight = Ink.BodySize(size) * 1.45,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };

        foreach (var line in text.Split('\n'))
            doc.Blocks.Add(Line(line.TrimEnd('\r'), palette, size));

        return doc;
    }

    private static Paragraph Line(string line, NoteColor palette, double size)
    {
        var p = new Paragraph { Margin = new Thickness(0) };
        var ink = palette.Ink;
        var attrs = new CharStyle[line.Length];

        double fontSize = Ink.BodySize(size);
        double alpha = 1.0;
        var lineBold = false;
        var lineItalic = false;
        var lineStrike = false;

        // Tasks first at line level, Markdown on top, so a completed task still reads
        // as done even when its line also carries emphasis.
        var isDoneTask = TaskSyntax.Marker(line) == TaskSyntax.Done;
        if (isDoneTask)
        {
            lineStrike = true;
            alpha = 0.45;
        }

        if (Settings.MarkdownStyling && line.Length > 0)
        {
            // # heading — bigger and bolder, hashes dimmed
            var heading = Regex.Match(line, @"^(#{1,6})([ \t]*)(\S.*)$");
            if (heading.Success)
            {
                var level = heading.Groups[1].Length;
                fontSize = Ink.BodySize(size) + Math.Max(1.5, 7 - level * 1.1);
                lineBold = true;
                Dim(attrs, heading.Groups[1]);
                Dim(attrs, heading.Groups[2]);
            }

            // > quote
            var quote = Regex.Match(line, @"^(>)([ \t]?)(.*)$");
            if (quote.Success)
            {
                alpha = Math.Min(alpha, 0.62);
                lineItalic = true;
                Dim(attrs, quote.Groups[1]);
                Dim(attrs, quote.Groups[2]);
            }

            // - bullet
            var bullet = Regex.Match(line, @"^([ \t]*)([-*+])([ \t]+)");
            if (bullet.Success)
            {
                Dim(attrs, bullet.Groups[2]);
                Dim(attrs, bullet.Groups[3]);
            }

            // **bold** / __bold__
            foreach (Match m in Regex.Matches(line, @"(\*\*|__)(?=\S)(.+?)(?<=\S)\1"))
            {
                Apply(attrs, m.Groups[2], a => a with { Bold = true });
                Dim(attrs, m.Index, 2);
                Dim(attrs, m.Index + m.Length - 2, 2);
            }

            // *italic* / _italic_
            foreach (Match m in Regex.Matches(line,
                         @"(?<![\*_])([\*_])(?=[^\*_\s])(.+?)(?<=[^\*_\s])\1(?![\*_])"))
            {
                Apply(attrs, m.Groups[2], a => a with { Italic = true });
                Dim(attrs, m.Index, 1);
                Dim(attrs, m.Index + m.Length - 1, 1);
            }

            // `code`
            foreach (Match m in Regex.Matches(line, @"`([^`]+)`"))
            {
                Apply(attrs, m.Groups[1], a => a with { Code = true });
                Dim(attrs, m.Index, 1);
                Dim(attrs, m.Index + m.Length - 1, 1);
            }

            // ~~struck~~; matching the delimiter length also handles ~~~text~~~.
            foreach (Match m in Regex.Matches(line,
                         @"(?<marker>~{2,})(?=\S)(?<body>.+?)(?<=\S)\k<marker>"))
            {
                var markerLength = m.Groups["marker"].Length;
                Apply(attrs, m.Groups["body"], a => a with { Strike = true });
                Dim(attrs, m.Index, markerLength);
                Dim(attrs, m.Index + m.Length - markerLength, markerLength);
            }
        }

        p.FontSize = fontSize;
        p.LineHeight = fontSize * 1.45;

        if (line.Length == 0)
        {
            p.Inlines.Add(new Run(""));
            return p;
        }

        var body = NoteColor.Tint(ink, alpha);
        var codeBg = NoteColor.Tint(ink, 0.07);

        // The ☐ and ☑ come from whatever font happens to carry them, and a hand like
        // Segoe Script carries neither — so each box fell back separately and the
        // ticked one came out a different size from the empty one. Both are drawn
        // from one face that has the pair.
        var start = 0;
        if (TaskSyntax.IsTask(line))
        {
            p.Inlines.Add(new Run(line[..1])
            {
                FontFamily = MarkerFace,
                FontSize = fontSize,
                Foreground = NoteColor.Tint(ink, isDoneTask ? 0.55 : 0.8),
            });
            start = 1;
        }

        var scripts = ScriptsOf(line);

        for (var i = start + 1; i <= line.Length; i++)
        {
            // A run breaks on a change of style *or* of writing system: the run is
            // what carries the language the spell checker uses, and one language for
            // the whole note means the other one is underlined word for word.
            if (i < line.Length && attrs[i].Equals(attrs[start]) && scripts[i] == scripts[start])
                continue;
            var a = attrs[start];
            var run = new Run(line[start..i])
            {
                Foreground = a.Dim ? Brushes.Transparent : body,
                FontSize = a.Dim ? HiddenMarkerSize : fontSize,
                FontWeight = a.Bold || lineBold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = a.Italic || lineItalic ? FontStyles.Italic : FontStyles.Normal,
                Language = SpellLanguages.ForScript(scripts[start]),
            };
            if (a.Code)
            {
                run.FontFamily = new FontFamily("Consolas");
                run.FontSize = fontSize - 0.5;
                run.Background = codeBg;
            }
            if (a.Strike || lineStrike) run.TextDecorations = TextDecorations.Strikethrough;
            p.Inlines.Add(run);
            start = i;
        }
        return p;
    }

    /// The script of every character, with digits, punctuation and spaces taking the
    /// script of the text they sit among — so "Что тебе надо?" stays one run and does
    /// not break at the question mark.
    private static SpellLanguages.Script[] ScriptsOf(string line)
    {
        var scripts = new SpellLanguages.Script[line.Length];
        var carried = SpellLanguages.Script.Neutral;
        var firstLetter = -1;

        for (var i = 0; i < line.Length; i++)
        {
            var s = SpellLanguages.ScriptOf(line[i]);
            if (s == SpellLanguages.Script.Neutral)
            {
                scripts[i] = carried;
            }
            else
            {
                scripts[i] = s;
                carried = s;
                if (firstLetter < 0) firstLetter = i;
            }
        }

        // Whatever came before the first letter belongs with it, not with nothing.
        if (firstLetter > 0)
            for (var i = 0; i < firstLetter; i++)
                scripts[i] = scripts[firstLetter];

        return scripts;
    }

    private static void Apply(CharStyle[] attrs, Group g, Func<CharStyle, CharStyle> f)
    {
        for (var i = g.Index; i < g.Index + g.Length && i < attrs.Length; i++)
            attrs[i] = f(attrs[i]);
    }

    private static void Dim(CharStyle[] attrs, Group g) => Dim(attrs, g.Index, g.Length);

    private static void Dim(CharStyle[] attrs, int index, int length)
    {
        for (var i = index; i < index + length && i < attrs.Length; i++)
            if (i >= 0) attrs[i] = attrs[i] with { Dim = true };
    }
}
