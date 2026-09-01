using Noty.Core;
using NUnit.Framework;

namespace Noty.Tests;

public sealed class TaskSyntaxTests
{
    [TestCase("☐ buy milk", TaskSyntax.Open)]
    [TestCase("☑ shipped", TaskSyntax.Done)]
    [TestCase("plain text", null)]
    [TestCase("", null)]
    public void Marker_recognizes_only_a_leading_checkbox(string line, char? expected) =>
        Assert.That(TaskSyntax.Marker(line), Is.EqualTo(expected));

    [TestCase("☐   buy milk", "buy milk")]
    [TestCase("☑ done", "done")]
    [TestCase("plain", "plain")]
    public void Stripped_removes_checkbox_and_spacing(string line, string expected) =>
        Assert.That(TaskSyntax.Stripped(line), Is.EqualTo(expected));

    [Test]
    public void FromMarkdown_converts_open_done_and_indented_tasks()
    {
        const string markdown = "- [ ] open\n  * [x] done\n* [X] also done\nnot a task";

        var result = TaskSyntax.FromMarkdown(markdown);

        Assert.That(result, Is.EqualTo("☐ open\n  ☑ done\n☑ also done\nnot a task"));
    }

    [Test]
    public void Markdown_conversion_round_trips_Noty_tasks()
    {
        const string body = "☐ open\n☑ done\nplain";

        Assert.That(TaskSyntax.FromMarkdown(TaskSyntax.ToMarkdown(body)), Is.EqualTo(body));
    }
}
