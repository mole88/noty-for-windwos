using Noty.Core;
using NUnit.Framework;

namespace Noty.Tests;

public sealed class NoteTests
{
    [TestCase("# Heading\nBody", "Heading")]
    [TestCase("☐ task title\nBody", "task title")]
    [TestCase("## ☑ completed\nBody", "completed")]
    [TestCase("\nSecond line", "")]
    public void DerivedTitle_cleans_the_first_line(string body, string expected) =>
        Assert.That(Note.DerivedTitle(body), Is.EqualTo(expected));

    [Test]
    public void DerivedTitle_truncates_long_titles()
    {
        var title = new string('a', 80);

        var result = Note.DerivedTitle(title);

        Assert.That(result, Has.Length.EqualTo(61));
        Assert.That(result, Does.EndWith("…"));
    }

    [Test]
    public void DisplayTitle_uses_placeholder_only_for_an_empty_title()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new Note { Title = "" }.DisplayTitle, Is.EqualTo("New note"));
            Assert.That(new Note { Title = "Plan" }.DisplayTitle, Is.EqualTo("Plan"));
        });
    }

    [Test]
    public void TaskProgress_counts_open_and_completed_lines()
    {
        var note = new Note { Body = "☐ first\nplain\n☑ second\n☑ third" };

        Assert.That(note.TaskProgress, Is.EqualTo((2, 3)));
    }

    [Test]
    public void TaskProgress_is_null_without_tasks() =>
        Assert.That(new Note { Body = "plain" }.TaskProgress, Is.Null);

    [Test]
    public void Preview_uses_lines_after_the_title_and_truncates()
    {
        var note = new Note { Body = "Title\n" + new string('b', 130) };

        Assert.That(note.Preview, Has.Length.EqualTo(121));
        Assert.That(note.Preview, Does.EndWith("…"));
    }

    [Test]
    public void Copy_creates_an_independent_note_object()
    {
        var original = new Note { Id = "n1", Body = "body", Pinned = true };

        var copy = original.Copy();
        copy.Body = "changed";

        Assert.Multiple(() =>
        {
            Assert.That(copy, Is.Not.SameAs(original));
            Assert.That(copy.Id, Is.EqualTo(original.Id));
            Assert.That(original.Body, Is.EqualTo("body"));
        });
    }
}
