using System.Text;
using System.Windows.Documents;

namespace Noty.Editor;

/// Maps between the FlowDocument a RichTextBox edits and the plain string a note
/// actually is.
///
/// A note stays plain text — that is the whole point of the ☐/☑ prefixes and of
/// leaving Markdown markers in the body — so the document is a rendering of the
/// string, never the source of truth.
public static class DocMap
{
    /// Paragraphs join with "\n"; a soft break inside one counts as a newline too.
    public static string PlainText(FlowDocument doc)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var block in doc.Blocks)
        {
            if (!first) sb.Append('\n');
            first = false;
            if (block is not Paragraph p) continue;
            foreach (var inline in p.Inlines) Append(sb, inline);
        }
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, Inline inline)
    {
        switch (inline)
        {
            case Run r:
                sb.Append(r.Text);
                break;
            case LineBreak:
                sb.Append('\n');
                break;
            case Span s:
                foreach (var child in s.Inlines) Append(sb, child);
                break;
        }
    }

    /// Plain-text offset of a caret position, or the end of the text when the
    /// pointer is somewhere the walk cannot place.
    public static int IndexOf(FlowDocument doc, TextPointer caret)
    {
        var index = 0;
        var first = true;
        foreach (var block in doc.Blocks)
        {
            if (!first) index += 1;
            first = false;
            if (block is not Paragraph p) continue;

            var inParagraph = p.ContentStart.CompareTo(caret) <= 0 &&
                              caret.CompareTo(p.ContentEnd) <= 0;
            foreach (var inline in p.Inlines)
            {
                if (inline is Run r)
                {
                    if (inParagraph &&
                        r.ContentStart.CompareTo(caret) <= 0 &&
                        caret.CompareTo(r.ContentEnd) <= 0)
                    {
                        return index + Math.Max(0, r.ContentStart.GetOffsetToPosition(caret));
                    }
                    index += r.Text.Length;
                }
                else if (inline is LineBreak)
                {
                    index += 1;
                }
            }
            if (inParagraph) return index;
        }
        return index;
    }

    /// The caret position for a plain-text offset.
    public static TextPointer PointerAt(FlowDocument doc, int target)
    {
        var index = 0;
        var first = true;
        TextPointer last = doc.ContentStart;

        foreach (var block in doc.Blocks)
        {
            if (!first) index += 1;
            first = false;
            if (block is not Paragraph p) continue;
            last = p.ContentEnd;

            if (p.Inlines.Count == 0)
            {
                if (index >= target) return p.ContentStart;
                continue;
            }
            foreach (var inline in p.Inlines)
            {
                if (inline is Run r)
                {
                    if (target <= index + r.Text.Length)
                        return r.ContentStart.GetPositionAtOffset(target - index) ?? r.ContentEnd;
                    index += r.Text.Length;
                    last = r.ContentEnd;
                }
                else if (inline is LineBreak)
                {
                    if (target <= index) return inline.ContentStart;
                    index += 1;
                }
            }
            if (target <= index) return p.ContentEnd;
        }
        return last;
    }

    /// Start and length of the line holding `index`, in plain-text coordinates.
    public static (int Start, int Length) LineRange(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        var start = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        if (index == 0) start = 0;
        var end = text.IndexOf('\n', index);
        if (end < 0) end = text.Length;
        return (start, end - start);
    }

    public static string LineAt(string text, int index)
    {
        var (s, len) = LineRange(text, index);
        return text.Substring(s, len);
    }
}
