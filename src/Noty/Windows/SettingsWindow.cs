using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Noty.Core;
using Noty.Services;

namespace Noty.Windows;

/// Rebind the shortcuts, pick the note face and size, set how far from the edge the
/// pointer wakes the deck, and switch Markdown styling on or off. Everything applies
/// immediately.
public sealed class SettingsWindow : Window
{
    private readonly StackPanel _body = new();
    private bool _recordingShortcut;

    public SettingsWindow()
    {
        Title = "Noty — Settings";
        Width = 520;
        Height = 640;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22));
        Foreground = Brushes.White;
        FontFamily = Ink.SystemFace;
        Theme.Apply(this);

        _body.Margin = new Thickness(18);
        Content = new ScrollViewer
        {
            Content = _body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None
                                    && !_recordingShortcut) Close();
        };

        Build();
    }

    private void Build()
    {
        _body.Children.Clear();

        _body.Children.Add(Heading("Shortcuts"));
        _body.Children.Add(Hint("Click a shortcut and press the keys you want. " +
                                "Global ones need no special permission."));
        _body.Children.Add(ShortcutRow("New note", () => Settings.ScNewNote, v => Settings.ScNewNote = v, global: true));
        _body.Children.Add(ShortcutRow("All Notes", () => Settings.ScAllNotes, v => Settings.ScAllNotes = v, global: true));
        _body.Children.Add(ShortcutRow("Archive", () => Settings.ScArchive, v => Settings.ScArchive = v, global: true));

        _body.Children.Add(Heading("Inside a note"));
        _body.Children.Add(ShortcutRow("Close", () => Settings.ScClose, v => Settings.ScClose = v,
            allowPlainEscape: true));
        _body.Children.Add(ShortcutRow("Find in note", () => Settings.ScFind, v => Settings.ScFind = v));
        _body.Children.Add(ShortcutRow("Task on / off", () => Settings.ScTask, v => Settings.ScTask = v));
        _body.Children.Add(ShortcutRow("Cycle colour", () => Settings.ScColour, v => Settings.ScColour = v));
        _body.Children.Add(ShortcutRow("Pin", () => Settings.ScPin, v => Settings.ScPin = v));
        _body.Children.Add(ShortcutRow("Archive note", () => Settings.ScArchiveNote, v => Settings.ScArchiveNote = v));
        _body.Children.Add(ShortcutRow("Delete", () => Settings.ScDelete, v => Settings.ScDelete = v));
        _body.Children.Add(ShortcutRow("Bigger text", () => Settings.ScBigger, v => Settings.ScBigger = v));
        _body.Children.Add(ShortcutRow("Smaller text", () => Settings.ScSmaller, v => Settings.ScSmaller = v));

        _body.Children.Add(Heading("The note"));

        var faces = new ComboBox { Margin = new Thickness(0, 4, 0, 10) };
        foreach (var f in Ink.Faces) faces.Items.Add(f.Name);
        faces.SelectedIndex = Math.Max(0, Ink.Faces.ToList().FindIndex(f => f.Family == Ink.Face.Family));
        faces.SelectionChanged += (_, _) =>
        {
            if (faces.SelectedIndex >= 0) Actions.SetNoteFont(Ink.Faces[faces.SelectedIndex].Family);
        };
        _body.Children.Add(Labelled("Note face", faces));

        var size = new Slider
        {
            Minimum = Settings.FontMin,
            Maximum = Settings.FontMax,
            Value = Settings.NoteFontSize,
            TickFrequency = 0.5,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 4, 0, 10),
        };
        var sizeLabel = new TextBlock
        {
            Text = $"{Settings.NoteFontSize:0.#} pt",
            Foreground = NoteColor.Tint(Colors.White, 0.6),
            FontSize = 11,
        };
        size.ValueChanged += (_, e) =>
        {
            Actions.SetFontSize(e.NewValue);
            sizeLabel.Text = $"{e.NewValue:0.#} pt";
        };
        _body.Children.Add(Labelled("Text size", size, sizeLabel));

        _body.Children.Add(Heading("The deck"));

        var edge = new ComboBox { Margin = new Thickness(0, 4, 0, 10) };
        foreach (var (name, width) in Settings.EdgeWidths) edge.Items.Add($"{name} — {width} px");
        edge.SelectedIndex = Math.Max(0, Array.FindIndex(Settings.EdgeWidths,
            e => Math.Abs(e.Width - Settings.EdgeWidth) < 0.01));
        edge.SelectionChanged += (_, _) =>
        {
            if (edge.SelectedIndex < 0) return;
            Settings.EdgeWidth = Settings.EdgeWidths[edge.SelectedIndex].Width;
            Actions.Refresh();
        };
        _body.Children.Add(Labelled("Wake the deck within", edge));

        var style = new ComboBox { Margin = new Thickness(0, 4, 0, 10) };
        foreach (DeckStyle s in Enum.GetValues<DeckStyle>()) style.Items.Add(s.Title());
        style.SelectedIndex = (int)Settings.DeckStyle;
        style.SelectionChanged += (_, _) =>
        {
            if (style.SelectedIndex >= 0) Actions.SetDeckStyle((DeckStyle)style.SelectedIndex);
        };
        _body.Children.Add(Labelled("Deck style", style));

        var scale = new Slider
        {
            Minimum = Settings.DeckScaleMin,
            Maximum = Settings.DeckScaleMax,
            Value = Settings.DeckScale,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 4, 0, 10),
        };
        var scaleLabel = new TextBlock
        {
            Text = $"{Settings.DeckScale * 100:0} %",
            Foreground = NoteColor.Tint(Colors.White, 0.6),
            FontSize = 11,
        };
        scale.ValueChanged += (_, e) =>
        {
            Actions.SetDeckScale(e.NewValue);
            scaleLabel.Text = $"{e.NewValue * 100:0} %";
        };
        _body.Children.Add(Labelled("Deck size", scale, scaleLabel));

        _body.Children.Add(Toggle("Keep the deck fanned out", Settings.KeepFanned,
            _ => Actions.ToggleKeepFanned()));
        _body.Children.Add(Toggle("Dock the deck to the left edge", Settings.DeckOnLeftEdge,
            _ => Actions.ToggleDeckEdge()));
        _body.Children.Add(Toggle("Keep the deck over full-screen apps", Settings.ShowOverFullScreen,
            _ => Actions.ToggleOverFullScreen()));
        _body.Children.Add(Toggle("Style Markdown as you type", Settings.MarkdownStyling, v =>
        {
            Settings.MarkdownStyling = v;
            Actions.Refresh();
        }));
        _body.Children.Add(Toggle("Launch at login", Settings.LaunchAtLogin,
            _ => Actions.ToggleLaunchAtLogin()));
    }

    // MARK: Bits

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White,
        Margin = new Thickness(0, 16, 0, 6),
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = NoteColor.Tint(Colors.White, 0.45),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private static FrameworkElement Labelled(string title, params FrameworkElement[] controls)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            Foreground = NoteColor.Tint(Colors.White, 0.8),
        });
        foreach (var c in controls) stack.Children.Add(c);
        return stack;
    }

    private static FrameworkElement Toggle(string title, bool value, Action<bool> set)
    {
        var box = new CheckBox
        {
            Content = title,
            IsChecked = value,
            Foreground = NoteColor.Tint(Colors.White, 0.85),
            Margin = new Thickness(0, 6, 0, 0),
        };
        box.Checked += (_, _) => set(true);
        box.Unchecked += (_, _) => set(false);
        return box;
    }

    /// A field that shows a shortcut and records the next chord pressed into it.
    private FrameworkElement ShortcutRow(string title, Func<Shortcut> get, Action<Shortcut> set,
                                         bool global = false, bool allowPlainEscape = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

        grid.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            Foreground = NoteColor.Tint(Colors.White, 0.85),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var field = new Button
        {
            Content = get().ToString(),
            Focusable = true,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = Cursors.Hand,
            ToolTip = "Click, then press the keys",
        };
        Grid.SetColumn(field, 1);

        var recording = false;
        field.Click += (_, _) =>
        {
            recording = true;
            _recordingShortcut = true;
            field.Content = "Press keys…";
            field.Focus();
        };
        field.LostFocus += (_, _) =>
        {
            recording = false;
            _recordingShortcut = false;
            field.Content = get().ToString();
        };
        field.PreviewKeyDown += (_, e) =>
        {
            if (!recording) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

            var candidate = new Shortcut(Keyboard.Modifiers, key);
            var error = ValidateShortcut(title, candidate, global, allowPlainEscape);
            if (error is not null)
            {
                recording = false;
                _recordingShortcut = false;
                field.Content = get().ToString();
                MessageBox.Show(this, error, "Shortcut unavailable",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!candidate.Equals(get())) set(candidate);
            recording = false;
            _recordingShortcut = false;
            field.Content = get().ToString();
            if (global) Actions.ShortcutsChanged();
        };

        grid.Children.Add(field);
        return grid;
    }

    private static string? ValidateShortcut(string title, Shortcut candidate, bool global,
                                            bool allowPlainEscape)
    {
        // A global shortcut without a modifier would take an ordinary typing key
        // away from every application on the desktop.
        if (global && candidate.Modifiers == ModifierKeys.None)
            return "Global shortcuts must include Ctrl, Alt, Shift, or Win.";

        // Plain Esc is the note-closing gesture. It remains a valid default for the
        // Close command itself, but cannot be reassigned to another in-note action.
        if (!global && !allowPlainEscape && candidate.Key == Key.Escape
                    && candidate.Modifiers == ModifierKeys.None)
            return "Esc is reserved for closing the note. Choose a modified shortcut instead.";

        foreach (var (otherTitle, shortcut) in AllShortcuts())
        {
            if (otherTitle == title) continue;
            if (shortcut.Equals(candidate))
                return $"{candidate} is already assigned to “{otherTitle}”. " +
                       "Choose a different shortcut.";
        }

        return null;
    }

    private static IEnumerable<(string Title, Shortcut Shortcut)> AllShortcuts()
    {
        yield return ("New note", Settings.ScNewNote);
        yield return ("All Notes", Settings.ScAllNotes);
        yield return ("Archive", Settings.ScArchive);
        yield return ("Close", Settings.ScClose);
        yield return ("Find in note", Settings.ScFind);
        yield return ("Task on / off", Settings.ScTask);
        yield return ("Cycle colour", Settings.ScColour);
        yield return ("Pin", Settings.ScPin);
        yield return ("Archive note", Settings.ScArchiveNote);
        yield return ("Delete", Settings.ScDelete);
        yield return ("Bigger text", Settings.ScBigger);
        yield return ("Smaller text", Settings.ScSmaller);
    }
}
