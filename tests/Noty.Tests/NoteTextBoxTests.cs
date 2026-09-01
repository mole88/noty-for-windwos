using System.Threading;
using System.Windows;
using Noty.Core;
using Noty.Editor;
using NUnit.Framework;

namespace Noty.Tests;

[Apartment(ApartmentState.STA)]
public sealed class NoteTextBoxTests
{
    [OneTimeSetUp]
    public void StartWpf()
    {
        if (Application.Current is null) _ = new Application();
    }

    [Test]
    public void Load_and_SetBody_preserve_text_and_caret()
    {
        var editor = Editor("first");

        editor.SetBody("second line", 6);

        Assert.Multiple(() =>
        {
            Assert.That(editor.PlainText, Is.EqualTo("second line"));
            Assert.That(editor.CaretIndex, Is.EqualTo(6));
        });
    }

    [Test]
    public void Undo_and_redo_restore_plain_text_snapshots()
    {
        var editor = Editor("before");
        editor.SetBody("after", 5);

        editor.UndoEdit();
        Assert.That(editor.PlainText, Is.EqualTo("before"));

        editor.RedoEdit();
        Assert.That(editor.PlainText, Is.EqualTo("after"));
    }

    [Test]
    public void ToggleTaskLine_adds_and_removes_a_checkbox()
    {
        var editor = Editor("first\nsecond");
        editor.CaretIndex = 8;

        editor.ToggleTaskLine();
        Assert.That(editor.PlainText, Is.EqualTo("first\n☐ second"));

        editor.ToggleTaskLine();
        Assert.That(editor.PlainText, Is.EqualTo("first\nsecond"));
    }

    [Test]
    public void Paste_handler_replaces_rich_formats_with_normalized_plain_text()
    {
        var editor = Editor("");
        var source = new DataObject();
        source.SetData(DataFormats.UnicodeText, "first\r\nsecond");
        source.SetData(DataFormats.Rtf, @"{\rtf1\b formatted}");
        var args = new DataObjectPastingEventArgs(source, false, DataFormats.Rtf);

        editor.RaiseEvent(args);

        Assert.Multiple(() =>
        {
            Assert.That(args.CommandCancelled, Is.False);
            Assert.That(args.FormatToApply, Is.EqualTo(DataFormats.UnicodeText));
            Assert.That(args.DataObject.GetData(DataFormats.UnicodeText), Is.EqualTo("first\nsecond"));
            Assert.That(args.DataObject.GetDataPresent(DataFormats.Rtf), Is.False);
        });
    }

    [Test]
    public void Paste_handler_rejects_a_clipboard_without_text()
    {
        var editor = Editor("");
        var source = new DataObject(DataFormats.Bitmap, new object());
        var args = new DataObjectPastingEventArgs(source, false, DataFormats.Bitmap);

        editor.RaiseEvent(args);

        Assert.That(args.CommandCancelled, Is.True);
    }

    private static NoteTextBox Editor(string text)
    {
        var editor = new NoteTextBox();
        editor.Load(text, NoteColor.At(0), 13.5);
        return editor;
    }
}
