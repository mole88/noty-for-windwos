using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Noty.Core;
using Noty.Editor;
using Noty.Interop;
using NUnit.Framework;

namespace Noty.Tests;

[Apartment(ApartmentState.STA)]
public sealed class DocMapAndStylerTests
{
    [Test]
    public void PlainText_reads_runs_spans_line_breaks_and_paragraphs()
    {
        var first = new Paragraph();
        first.Inlines.Add(new Run("one"));
        first.Inlines.Add(new Span(new Run(" two")));
        first.Inlines.Add(new LineBreak());
        first.Inlines.Add(new Run("three"));
        var document = new FlowDocument(first);
        document.Blocks.Add(new Paragraph(new Run("four")));

        Assert.That(DocMap.PlainText(document), Is.EqualTo("one two\nthree\nfour"));
    }

    [Test]
    public void Pointer_mapping_round_trips_every_plain_text_offset()
    {
        const string text = "first line\nsecond **bold** line\n";
        var document = Styler.Build(text, NoteColor.At(0), 13.5);

        for (var index = 0; index <= text.Length; index++)
        {
            var pointer = DocMap.PointerAt(document, index);
            Assert.That(DocMap.IndexOf(document, pointer), Is.EqualTo(index), $"offset {index}");
        }
    }

    [TestCase("one\ntwo\nthree", 0, 0, 3)]
    [TestCase("one\ntwo\nthree", 4, 4, 3)]
    [TestCase("one\ntwo\nthree", 13, 8, 5)]
    public void LineRange_returns_the_line_containing_the_offset(
        string text, int index, int expectedStart, int expectedLength) =>
        Assert.That(DocMap.LineRange(text, index), Is.EqualTo((expectedStart, expectedLength)));

    [Test]
    public void Styler_preserves_the_exact_plain_text()
    {
        const string text = "# Heading\n☐ task\n> quote with `code` and **bold**";

        var document = Styler.Build(text, NoteColor.At(2), 15.5);

        Assert.That(DocMap.PlainText(document), Is.EqualTo(text));
    }

    [Test]
    public void Styler_applies_bold_code_and_hidden_markers()
    {
        var document = Styler.Build("**bold** and `code`", NoteColor.At(0), 13.5);
        var runs = document.Blocks.OfType<Paragraph>().Single().Inlines.OfType<Run>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(runs.Single(r => r.Text == "bold").FontWeight, Is.EqualTo(FontWeights.Bold));
            Assert.That(runs.Single(r => r.Text == "code").FontFamily.Source, Does.Contain("Consolas"));
            Assert.That(runs.Where(r => r.Text.Contains('*') || r.Text.Contains('`'))
                            .All(r => ReferenceEquals(r.Foreground, Brushes.Transparent)), Is.True);
        });
    }

    [Test]
    public void Styler_strikes_and_dims_a_completed_task()
    {
        var document = Styler.Build("☑ finished", NoteColor.At(1), 13.5);
        var runs = document.Blocks.OfType<Paragraph>().Single().Inlines.OfType<Run>().ToList();
        var body = runs.Single(r => r.Text.Contains("finished"));

        Assert.Multiple(() =>
        {
            Assert.That(runs[0].Text, Is.EqualTo("☑"));
            Assert.That(body.TextDecorations, Is.Not.Null.And.Not.Empty);
            Assert.That(((SolidColorBrush)body.Foreground).Color.A, Is.LessThan(255));
        });
    }

    [TestCase('A', SpellLanguages.Script.Latin)]
    [TestCase('é', SpellLanguages.Script.Latin)]
    [TestCase('Я', SpellLanguages.Script.Cyrillic)]
    [TestCase('ї', SpellLanguages.Script.Cyrillic)]
    [TestCase('7', SpellLanguages.Script.Neutral)]
    [TestCase('?', SpellLanguages.Script.Neutral)]
    public void Script_detection_classifies_letters_and_neutral_characters(
        char value, SpellLanguages.Script expected) =>
        Assert.That(SpellLanguages.ScriptOf(value), Is.EqualTo(expected));
}
