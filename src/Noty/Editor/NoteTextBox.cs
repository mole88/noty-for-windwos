using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Noty.Core;
using Noty.Interop;

namespace Noty.Editor;

/// A text view that treats a leading ☐ / ☑ as a real checkbox: clicking the box
/// toggles it, Return carries the list on, and finished lines get struck through.
///
/// The note is a plain string; the document is only its rendering. Restyling
/// rebuilds the document, so the caret has to be put back afterwards — the same
/// care the original takes when it re-runs its attribute pass on every keystroke.
public sealed class NoteTextBox : RichTextBox
{
    private readonly DispatcherTimer _restyle;
    private readonly List<(string Text, int Caret)> _undo = new();
    private readonly List<(string Text, int Caret)> _redo = new();
    private const int UndoDepth = 200;
    private static readonly TimeSpan EditSeriesTimeout = TimeSpan.FromSeconds(1.2);

    private NoteColor _palette = NoteColor.At(0);
    private double _size = 13.5;
    private bool _suspend;
    private string _lastText = "";
    private EditSeries? _editSeries;

    private enum EditKind
    {
        InsertWord,
        InsertWhitespace,
        InsertPunctuation,
        DeleteWord,
        DeleteWhitespace,
        DeletePunctuation,
    }

    private readonly record struct TextEdit(int Start, string Removed, string Inserted);
    private readonly record struct EditSeries(
        EditKind Kind, int Start, int End, DateTime LastEdit, bool ContainsWord);

    /// Raised with the plain body whenever it actually changed.
    public event EventHandler<string>? BodyChanged;

    public NoteTextBox()
    {
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        AcceptsTab = true;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Padding = new Thickness(0);
        // The RichTextBox's own undo cannot survive a document rebuild, so Noty
        // keeps plain-text snapshots instead.
        IsUndoEnabled = false;
        Document = new FlowDocument();

        // Restyling replaces the document under the caret, so it waits for a real
        // pause in typing. At a couple of hundred milliseconds it fired between
        // ordinary keystrokes and swallowed some of them.
        _restyle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _restyle.Tick += (_, _) =>
        {
            _restyle.Stop();
            Restyle();
        };

        TextChanged += OnTextChanged;
        DataObject.AddPastingHandler(this, OnPasting);

        FollowInputLanguage();
        Loaded += (_, _) => InputLanguageManager.Current.InputLanguageChanged += OnInputLanguageChanged;
        Unloaded += (_, _) => InputLanguageManager.Current.InputLanguageChanged -= OnInputLanguageChanged;
    }

    private void OnInputLanguageChanged(object? sender, InputLanguageEventArgs e) => FollowInputLanguage();

    /// Spell-check in the language being typed.
    ///
    /// WPF leaves `Language` at the framework default no matter what the keyboard is
    /// set to, so anything but English came back underlined word for word. The view
    /// follows the layout instead, and checking is switched off outright for a
    /// language Windows has no dictionary for — no marks at all beats every word
    /// marked wrong.
    private void FollowInputLanguage()
    {
        var tag = InputLanguageManager.Current.CurrentInputLanguage?.IetfLanguageTag;
        if (string.IsNullOrEmpty(tag)) tag = CultureInfo.CurrentCulture.IetfLanguageTag;
        try
        {
            Language = XmlLanguage.GetLanguage(tag);
        }
        catch (Exception e)
        {
            Log.Line($"unknown input language {tag} — {e.Message}");
            return;
        }
        SpellCheck.IsEnabled = SpellLanguages.Supported(tag);
    }

    public string PlainText => DocMap.PlainText(Document);

    public int CaretIndex
    {
        get => DocMap.IndexOf(Document, CaretPosition);
        set => CaretPosition = DocMap.PointerAt(Document, Math.Max(0, value));
    }

    /// Load a note into the view — a full rebuild, caret at the top.
    public void Load(string text, NoteColor palette, double size)
    {
        _restyle.Stop();
        _palette = palette;
        _size = size;
        _lastText = text;
        _editSeries = null;
        _undo.Clear();
        _redo.Clear();
        Rebuild(text, 0);
        CaretBrush = palette.InkBrush;
    }

    public void Restyle(NoteColor palette, double size)
    {
        _palette = palette;
        _size = size;
        CaretBrush = palette.InkBrush;
        Restyle();
    }

    private void Restyle()
    {
        var text = PlainText;
        Rebuild(text, CaretIndex);
    }

    private void Rebuild(string text, int caret)
    {
        _suspend = true;
        try
        {
            Document = Styler.Build(text, _palette, _size);
            CaretIndex = Math.Clamp(caret, 0, text.Length);
        }
        finally
        {
            _suspend = false;
        }
    }

    /// Replace the whole body from code — used by the checkbox toggle and Ctrl+T.
    public void SetBody(string text, int caret)
    {
        _editSeries = null;
        Push();
        Rebuild(text, caret);
        Emit(text);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suspend) return;
        var text = PlainText;
        if (text == _lastText) return;

        var edit = DescribeEdit(_lastText, text, CaretIndex);
        var kind = Classify(edit);
        var now = DateTime.UtcNow;
        if (kind is not { } editKind ||
            _editSeries is not { } series ||
            !Continues(series, editKind, edit, now))
        {
            // Store the text before the whole run, not before every character.
            // The inferred caret is only used when the snapshot is restored.
            Push(_lastText, edit.Start + edit.Removed.Length);
            _editSeries = kind is { } startKind
                ? NewSeries(startKind, edit, now)
                : null;
        }
        else
        {
            _redo.Clear();
            _editSeries = Extend(series, editKind, edit, now);
        }

        Emit(text);
        _restyle.Stop();
        _restyle.Start();
    }

    private void Emit(string text)
    {
        _lastText = text;
        BodyChanged?.Invoke(this, text);
    }

    // MARK: Undo — plain-text snapshots, since the document is rebuilt under us

    private void Push(string? text = null, int? caret = null)
    {
        _undo.Add((text ?? PlainText, caret ?? CaretIndex));
        if (_undo.Count > UndoDepth) _undo.RemoveAt(0);
        _redo.Clear();
    }

    private static TextEdit DescribeEdit(string before, string after, int caret)
    {
        // Prefer the caret for pure insertion/deletion. That disambiguates edits in
        // repeated text (adding an "a" in "aaa"), where a prefix/suffix diff alone
        // would put the change at an arbitrary matching character.
        var delta = after.Length - before.Length;
        if (delta > 0)
        {
            var start = Math.Clamp(caret - delta, 0, after.Length - delta);
            if (after.Remove(start, delta) == before)
                return new TextEdit(start, "", after.Substring(start, delta));
        }
        else if (delta < 0)
        {
            var length = -delta;
            var start = Math.Clamp(caret, 0, before.Length - length);
            if (before.Remove(start, length) == after)
                return new TextEdit(start, before.Substring(start, length), "");
        }

        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length &&
               before[prefix] == after[prefix]) prefix++;

        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix &&
               before[before.Length - suffix - 1] == after[after.Length - suffix - 1]) suffix++;

        return new TextEdit(prefix,
            before.Substring(prefix, before.Length - prefix - suffix),
            after.Substring(prefix, after.Length - prefix - suffix));
    }

    private static EditKind? Classify(TextEdit edit)
    {
        if (edit.Removed.Length == 0 && edit.Inserted.Length > 0 &&
            SameCharacterKind(edit.Inserted))
        {
            return CharacterKind(edit.Inserted[0]) switch
            {
                0 => EditKind.InsertWord,
                1 => EditKind.InsertWhitespace,
                _ => EditKind.InsertPunctuation,
            };
        }

        // A selection or Ctrl+Backspace is an operation in its own right. Only
        // single-character deletes are folded into a held Backspace/Delete series.
        if (edit.Inserted.Length == 0 && edit.Removed.Length == 1)
        {
            return CharacterKind(edit.Removed[0]) switch
            {
                0 => EditKind.DeleteWord,
                1 => EditKind.DeleteWhitespace,
                _ => EditKind.DeletePunctuation,
            };
        }
        return null;
    }

    private static bool SameCharacterKind(string value)
    {
        var kind = CharacterKind(value[0]);
        for (var i = 1; i < value.Length; i++)
            if (CharacterKind(value[i]) != kind) return false;
        return true;
    }

    /// 0 = word, 1 = whitespace, 2 = punctuation/symbol.
    private static int CharacterKind(char value)
    {
        if (char.IsWhiteSpace(value)) return 1;
        var category = char.GetUnicodeCategory(value);
        return char.IsLetterOrDigit(value) || value is '_' or '\'' or '’' ||
               category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
            ? 0
            : 2;
    }

    private static bool Continues(EditSeries series, EditKind kind, TextEdit edit, DateTime now)
    {
        var seriesInserts = IsInsertion(series.Kind);
        var editInserts = IsInsertion(kind);
        if (seriesInserts || editInserts)
        {
            if (!seriesInserts || !editInserts || edit.Start != series.End ||
                now - series.LastEdit > EditSeriesTimeout) return false;

            // Separators and punctuation never become otherwise invisible undo
            // steps. Ordinarily they trail the word just typed. When a separator
            // starts a series (for example after a paste), the following word is
            // folded into that same series instead.
            if (IsDelimiter(kind)) return true;
            if (kind == EditKind.InsertWord && IsDelimiter(series.Kind))
                return !series.ContainsWord;
            return series.Kind == kind;
        }

        // A held Backspace/Delete is one editing series even as it crosses a space
        // or punctuation. Delete keeps removing at one offset; Backspace walks left.
        return now - series.LastEdit <= EditSeriesTimeout &&
               (edit.Start == series.Start || edit.Start + edit.Removed.Length == series.Start);
    }

    private static EditSeries NewSeries(EditKind kind, TextEdit edit, DateTime now) =>
        new(kind, edit.Start, edit.Start + edit.Inserted.Length, now,
            kind == EditKind.InsertWord);

    private static EditSeries Extend(EditSeries series, EditKind kind, TextEdit edit, DateTime now)
    {
        if (IsInsertion(series.Kind))
            return series with
            {
                Kind = kind,
                End = edit.Start + edit.Inserted.Length,
                LastEdit = now,
                ContainsWord = series.ContainsWord || kind == EditKind.InsertWord,
            };
        return series with
        {
            Kind = kind,
            Start = Math.Min(series.Start, edit.Start),
            LastEdit = now,
        };
    }

    private static bool IsInsertion(EditKind kind) =>
        kind is EditKind.InsertWord or EditKind.InsertWhitespace or EditKind.InsertPunctuation;

    private static bool IsDelimiter(EditKind kind) =>
        kind is EditKind.InsertWhitespace or EditKind.InsertPunctuation;

    public void UndoEdit()
    {
        if (_undo.Count == 0) return;
        _restyle.Stop();
        _editSeries = null;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add((PlainText, CaretIndex));
        Rebuild(entry.Text, entry.Caret);
        Emit(entry.Text);
    }

    public void RedoEdit()
    {
        if (_redo.Count == 0) return;
        _restyle.Stop();
        _editSeries = null;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add((PlainText, CaretIndex));
        Rebuild(entry.Text, entry.Caret);
        Emit(entry.Text);
    }

    // MARK: Clipboard

    /// RichTextBox prefers RTF/HTML when both are on the clipboard. Notes are plain
    /// strings, so inserting that document structure would make DocMap skip tables,
    /// lists and images and the next style pass would silently discard them. Read
    /// the clipboard's text representation and perform one plain-text edit instead.
    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        string? value = null;
        try
        {
            if (e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
                value = e.SourceDataObject.GetData(DataFormats.UnicodeText, true) as string;
            else if (e.SourceDataObject.GetDataPresent(DataFormats.Text, true))
                value = e.SourceDataObject.GetData(DataFormats.Text, true) as string;
        }
        catch (Exception ex)
        {
            Log.Line($"clipboard paste failed — {ex.Message}");
        }

        // Suppress every non-text format, including a standalone bitmap.
        e.CancelCommand();
        e.Handled = true;
        if (value is null) return;

        value = value.Replace("\r\n", "\n").Replace('\r', '\n');
        var before = PlainText;
        var start = DocMap.IndexOf(Document, Selection.Start);
        var end = DocMap.IndexOf(Document, Selection.End);
        if (end < start) (start, end) = (end, start);
        start = Math.Clamp(start, 0, before.Length);
        end = Math.Clamp(end, start, before.Length);

        SetBody(before.Remove(start, end - start).Insert(start, value), start + value.Length);
    }

    // MARK: Tasks

    /// Turn the caret's line into a task, or strip the checkbox back off it.
    public void ToggleTaskLine()
    {
        var text = PlainText;
        var caret = CaretIndex;
        var (start, length) = DocMap.LineRange(text, caret);
        var line = text.Substring(start, length);

        string next;
        int newCaret;
        if (TaskSyntax.IsTask(line))
        {
            var strip = line.Length > 1 && line[1] == ' ' ? 2 : 1;
            next = text.Remove(start, strip);
            newCaret = Math.Max(start, caret - strip);
        }
        else
        {
            next = text.Insert(start, TaskSyntax.OpenPrefix);
            newCaret = caret + TaskSyntax.OpenPrefix.Length;
        }
        SetBody(next, newCaret);
    }

    /// Returns true when the click landed on a checkbox and was consumed.
    private bool ToggleBoxAt(Point point)
    {
        var pos = GetPositionFromPoint(point, true);
        if (pos is null) return false;
        var text = PlainText;
        var index = DocMap.IndexOf(Document, pos);
        var (start, length) = DocMap.LineRange(text, index);
        if (length == 0) return false;
        var marker = TaskSyntax.Marker(text.AsSpan(start, length));
        if (marker is null) return false;
        // Only the box itself, not the rest of the line.
        if (index > start + 1) return false;

        var flipped = marker == TaskSyntax.Open ? TaskSyntax.Done : TaskSyntax.Open;
        var next = text.Remove(start, 1).Insert(start, flipped.ToString());
        SetBody(next, index);
        return true;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (ToggleBoxAt(e.GetPosition(this)))
        {
            e.Handled = true;
            return;
        }
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            UndoEdit();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            RedoEdit();
            e.Handled = true;
            return;
        }

        // Return on a task line starts the next task; on an empty one, ends the list.
        if (e.Key == Key.Return && Keyboard.Modifiers == ModifierKeys.None)
        {
            var text = PlainText;
            var caret = CaretIndex;
            var (start, length) = DocMap.LineRange(text, caret);
            var line = text.Substring(start, length);
            if (TaskSyntax.IsTask(line))
            {
                if (TaskSyntax.Stripped(line).Trim().Length == 0)
                {
                    var next = text.Remove(start, length);
                    SetBody(next, start);
                }
                else
                {
                    var next = text.Insert(caret, "\n" + TaskSyntax.OpenPrefix);
                    SetBody(next, caret + 1 + TaskSyntax.OpenPrefix.Length);
                }
                e.Handled = true;
                return;
            }
        }
        base.OnPreviewKeyDown(e);
    }

    // MARK: Find

    public int CountMatches(string q)
    {
        if (string.IsNullOrEmpty(q)) return 0;
        var text = PlainText;
        var count = 0;
        var i = 0;
        while (i < text.Length)
        {
            var at = text.IndexOf(q, i, StringComparison.CurrentCultureIgnoreCase);
            if (at < 0) break;
            count++;
            i = at + Math.Max(1, q.Length);
        }
        return count;
    }

    public void FindNext(string q, bool forward = true)
    {
        if (string.IsNullOrEmpty(q)) return;
        var text = PlainText;
        var caret = CaretIndex;
        int at;
        if (forward)
        {
            var from = Math.Min(text.Length, caret + 1);
            at = text.IndexOf(q, from, StringComparison.CurrentCultureIgnoreCase);
            if (at < 0) at = text.IndexOf(q, 0, StringComparison.CurrentCultureIgnoreCase);
        }
        else
        {
            var from = Math.Max(0, caret - 1);
            at = from > 0 ? text.LastIndexOf(q, Math.Min(from, text.Length - 1),
                                            StringComparison.CurrentCultureIgnoreCase) : -1;
            if (at < 0 && text.Length > 0)
                at = text.LastIndexOf(q, text.Length - 1, StringComparison.CurrentCultureIgnoreCase);
        }
        if (at < 0) return;

        var start = DocMap.PointerAt(Document, at);
        var end = DocMap.PointerAt(Document, at + q.Length);
        Selection.Select(start, end);
        Focus();
        var rect = start.GetCharacterRect(LogicalDirection.Forward);
        ScrollToVerticalOffset(Math.Max(0, VerticalOffset + rect.Top - ActualHeight / 2));
    }
}
