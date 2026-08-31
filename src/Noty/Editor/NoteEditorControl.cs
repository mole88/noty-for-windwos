using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Noty.Core;
using Noty.Deck;
using Noty.Deck.Controls;
using Noty.Services;

namespace Noty.Editor;

/// The note pulled clear of the deck: its own tab carried along as a gutter, a
/// header, the body, and the palette in the footer.
public sealed class NoteEditorControl : Grid
{
    private Note _note;
    private readonly DeckController _controller;
    private readonly bool _onRight;
    private readonly NoteColor _pal;

    private readonly NoteTextBox _text = new();
    private readonly TextBlock _savedLabel = new();
    private readonly TextBlock _headerTitle = new();
    private readonly TextBlock _gutterLabel = new();
    private TextBlock? _pinGlyph;
    private readonly Border _findBar;
    private readonly TextBox _findField = new();
    private readonly TextBlock _findCount = new();

    private DispatcherTimer? _save;
    private readonly DispatcherTimer _savedLabelTimer = new();
    private DateTime? _savedAt;
    private double _appliedSize = Settings.NoteFontSize;
    private string _appliedFontName = Settings.NoteFontName;
    private bool _appliedMarkdownStyling = Settings.MarkdownStyling;

    public bool FindVisible => _findBar.Visibility == Visibility.Visible;

    public NoteEditorControl(Note note, DeckController controller, bool onRight)
    {
        _note = note;
        _controller = controller;
        _onRight = onRight;
        _pal = note.Palette;

        // Rounded where it leaves the deck, square where it meets the screen edge.
        var backdrop = new Path
        {
            // The note is paper, not tracing paper: keep the rounded exterior
            // transparent, but make every painted pixel of the sheet opaque.
            Fill = _pal.PaperBrush,
            Stroke = NoteColor.Tint(Colors.Black, 0.07),
            StrokeThickness = 0.5,
            Data = Shapes.EdgeTab(DeckGeom.EditorWidth, DeckGeom.EditorHeight, onRight, 14),
            Effect = Shapes.Shadow(0.34, 28, onRight ? -12 : 12, 12),
        };
        Children.Add(backdrop);

        Clip = Shapes.EdgeTab(DeckGeom.EditorWidth, DeckGeom.EditorHeight, onRight, 14);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = onRight ? new GridLength(DeckGeom.GutterWidth) : new GridLength(1, GridUnitType.Star),
        });
        body.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = onRight ? new GridLength(1, GridUnitType.Star) : new GridLength(DeckGeom.GutterWidth),
        });

        var gutter = BuildGutter();
        var sheet = BuildSheet(out _findBar);
        SetColumn(gutter, onRight ? 0 : 1);
        SetColumn(sheet, onRight ? 1 : 0);
        body.Children.Add(gutter);
        body.Children.Add(sheet);
        Children.Add(body);

        _text.Load(note.Body, _pal, Settings.NoteFontSize);
        _text.BodyChanged += (_, value) => ScheduleSave(value);
        _savedAt = note.Modified;
        _savedLabelTimer.Tick += (_, _) => UpdateSavedLabel();
        Loaded += (_, _) => UpdateSavedLabel();
        Unloaded += (_, _) => _savedLabelTimer.Stop();
        UpdateSavedLabel();

        PreviewKeyDown += OnPreviewKeyDown;
    }

    // MARK: The note's own tab, carried along so it reads as growing out of the deck

    private FrameworkElement BuildGutter()
    {
        var host = new Grid
        {
            Background = _pal.DashAt(0.20),
            ClipToBounds = true,
        };
        _gutterLabel.Text = _note.DisplayTitle.ToUpperInvariant();
        _gutterLabel.FontFamily = Ink.TabFamily;
        _gutterLabel.FontSize = Ink.TabFontSize;
        _gutterLabel.FontWeight = FontWeights.SemiBold;
        _gutterLabel.Foreground = _pal.InkAt(0.7);
        _gutterLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        _gutterLabel.TextAlignment = TextAlignment.Center;
        _gutterLabel.Width = DeckGeom.EditorHeight - 44;
        _gutterLabel.LayoutTransform = new RotateTransform(_onRight ? 90 : -90);
        _gutterLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _gutterLabel.VerticalAlignment = VerticalAlignment.Center;
        host.Children.Add(_gutterLabel);
        // Separated from the sheet by the same dashed rule the deck hangs from.
        var rule = Shapes.EdgeRule(DeckGeom.EditorHeight, _pal.InkAt(0.22));
        rule.HorizontalAlignment = _onRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        rule.Stretch = Stretch.Fill;
        host.Children.Add(rule);
        return host;
    }

    private FrameworkElement BuildSheet(out Border findBar)
    {
        var sheet = new Grid();
        sheet.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        sheet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sheet.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sheet.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

        var header = BuildHeader();
        SetRow(header, 0);
        sheet.Children.Add(header);

        findBar = BuildFindBar();
        SetRow(findBar, 1);
        sheet.Children.Add(findBar);

        _text.Margin = new Thickness(0);
        SetRow(_text, 2);
        sheet.Children.Add(_text);

        var footer = BuildFooter();
        SetRow(footer, 3);
        sheet.Children.Add(footer);
        return sheet;
    }

    private FrameworkElement BuildHeader()
    {
        var grid = new Grid { Margin = new Thickness(14, 0, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _headerTitle.Text = _note.DisplayTitle;
        _headerTitle.FontFamily = Ink.SystemFace;
        _headerTitle.FontSize = 12.5;
        _headerTitle.FontWeight = FontWeights.SemiBold;
        _headerTitle.Foreground = _pal.InkAt(0.92);
        _headerTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _headerTitle.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(_headerTitle);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetColumn(right, 1);

        _savedLabel.FontFamily = Ink.SystemFace;
        _savedLabel.FontSize = 10;
        _savedLabel.Foreground = _pal.InkAt(0.42);
        _savedLabel.VerticalAlignment = VerticalAlignment.Center;
        _savedLabel.Margin = new Thickness(0, 0, 8, 0);
        right.Children.Add(_savedLabel);

        var pin = IconButton(_note.Pinned ? "📌" : "📍",
            _note.Pinned ? $"Unpin — {Settings.ScPin}" : $"Pin so it stays open  {Settings.ScPin}",
            _pal.InkAt(_note.Pinned ? 0.85 : 0.4),
            () => NoteStore.Shared.TogglePin(_note.Id));
        _pinGlyph = (pin as Button)?.Content as TextBlock;
        right.Children.Add(pin);

        right.Children.Add(IconButton("☑", $"Task  {Settings.ScTask}", _pal.InkAt(0.5),
            ToggleTaskLine));

        right.Children.Add(IconButton("🔍", $"Find  {Settings.ScFind}", _pal.InkAt(0.5),
            ToggleFind));

        grid.Children.Add(right);
        return grid;
    }

    private Border BuildFindBar()
    {
        var bar = new Border
        {
            Background = _pal.DashAt(0.12),
            Height = 28,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(14, 0, 14, 0),
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _findField.Width = 250;
        _findField.BorderThickness = new Thickness(0);
        _findField.Background = Brushes.Transparent;
        _findField.Foreground = _pal.InkBrush;
        _findField.CaretBrush = _pal.InkBrush;
        _findField.FontFamily = Ink.SystemFace;
        _findField.FontSize = 12;
        _findField.VerticalAlignment = VerticalAlignment.Center;
        _findField.TextChanged += (_, _) =>
        {
            _findCount.Text = _text.CountMatches(_findField.Text) is var n && n == 0 ? "—" : n.ToString();
        };
        _findField.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Return) return;
            _text.FindNext(_findField.Text, forward: Keyboard.Modifiers != ModifierKeys.Shift);
            _findField.Focus();
            e.Handled = true;
        };

        row.Children.Add(new TextBlock
        {
            Text = "🔍",
            FontSize = 10,
            Foreground = _pal.InkAt(0.45),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        row.Children.Add(_findField);

        _findCount.Text = "—";
        _findCount.FontFamily = Ink.SystemFace;
        _findCount.FontSize = 10.5;
        _findCount.Foreground = _pal.InkAt(0.45);
        _findCount.VerticalAlignment = VerticalAlignment.Center;
        _findCount.Margin = new Thickness(6, 0, 6, 0);
        row.Children.Add(_findCount);

        row.Children.Add(IconButton("▲", "Previous", _pal.InkAt(0.55),
            () => _text.FindNext(_findField.Text, forward: false)));
        row.Children.Add(IconButton("▼", "Next", _pal.InkAt(0.55),
            () => _text.FindNext(_findField.Text)));

        bar.Child = row;
        return bar;
    }

    private FrameworkElement BuildFooter()
    {
        var grid = new Grid { Margin = new Thickness(14, 0, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var swatches = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        for (var i = 0; i < NoteColor.All.Count; i++)
        {
            var idx = i;
            var c = NoteColor.All[i];
            var dot = new Ellipse
            {
                Width = 11,
                Height = 11,
                Fill = c.DashBrush,
                Stroke = idx == _note.Color ? _pal.InkAt(0.55) : Brushes.Transparent,
                StrokeThickness = idx == _note.Color ? 1.5 : 0,
                Margin = new Thickness(3.5, 0, 3.5, 0),
                Cursor = Cursors.Hand,
                ToolTip = c.Name,
            };
            dot.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                NoteStore.Shared.SetColor(_note.Id, idx);
            };
            swatches.Children.Add(dot);
        }
        grid.Children.Add(swatches);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetColumn(buttons, 2);
        buttons.Children.Add(FooterButton("Archive", () =>
        {
            Flush();
            NoteStore.Shared.SetArchived(_note.Id, true);
            _controller.Collapse();
        }));
        buttons.Children.Add(FooterButton("Delete", () =>
        {
            NoteStore.Shared.Delete(_note.Id);
            _controller.Collapse();
        }));
        buttons.Children.Add(FooterButton("Close", () => _controller.Collapse()));
        grid.Children.Add(buttons);
        return grid;
    }

    private FrameworkElement IconButton(string glyph, string tip, Brush ink, Action action)
    {
        var b = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontSize = 11,
                Foreground = ink,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Focusable = false,
            Template = FlatButtonTemplate(),
        };
        b.Click += (_, _) => action();
        return b;
    }

    private FrameworkElement FooterButton(string title, Action action)
    {
        var b = new Button
        {
            Content = new TextBlock
            {
                Text = title,
                FontFamily = Ink.SystemFace,
                FontSize = 10.5,
                FontWeight = FontWeights.Medium,
                Foreground = _pal.InkAt(0.72),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Height = 20,
            MinWidth = 46,
            Margin = new Thickness(5, 0, 0, 0),
            Background = _pal.InkAt(0.08),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Focusable = false,
            Template = FlatButtonTemplate(6),
        };
        b.Click += (_, _) => action();
        return b;
    }

    /// A button that is only its content and a rounded tint — WPF's default chrome
    /// would read as a dialog control sitting on a sticky note.
    private static ControlTemplate FlatButtonTemplate(double radius = 0)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background")
            {
                RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent,
            });
        border.SetValue(Border.PaddingProperty, new Thickness(radius > 0 ? 8 : 0, 0, radius > 0 ? 8 : 0, 0));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    // MARK: Behaviour

    /// A redraw keeps this editor and only refreshes what the deck knows changed —
    /// the derived title, the pin, the saved stamp.
    public void RefreshChrome(Note note)
    {
        _note = note;
        _headerTitle.Text = note.DisplayTitle;
        _gutterLabel.Text = note.DisplayTitle.ToUpperInvariant();
        if (_pinGlyph is not null)
        {
            _pinGlyph.Text = note.Pinned ? "📌" : "📍";
            _pinGlyph.Foreground = _pal.InkAt(note.Pinned ? 0.85 : 0.4);
        }
        var size = Settings.NoteFontSize;
        var fontName = Settings.NoteFontName;
        var markdownStyling = Settings.MarkdownStyling;
        if (Math.Abs(_appliedSize - size) > 0.01 ||
            !string.Equals(_appliedFontName, fontName, StringComparison.Ordinal) ||
            _appliedMarkdownStyling != markdownStyling)
        {
            _appliedSize = size;
            _appliedFontName = fontName;
            _appliedMarkdownStyling = markdownStyling;
            _text.Restyle(_pal, _appliedSize);
            // The carried tab uses the selected note face too.
            _gutterLabel.FontFamily = Ink.TabFamily;
            _gutterLabel.FontSize = Ink.TabFontSize;
        }
        UpdateSavedLabel();
    }

    public void FocusText() =>
        Dispatcher.BeginInvoke(new Action(() => _text.Focus()), DispatcherPriority.Input);

    public void ToggleTaskLine() => _text.ToggleTaskLine();

    public void ToggleFind()
    {
        if (FindVisible) HideFind();
        else
        {
            _findBar.Visibility = Visibility.Visible;
            // The field has only just been made visible, so it cannot take focus yet
            // — and it is the keyboard focus that matters here, not the logical one,
            // or the field shows a caret and receives nothing.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _findField.Focus();
                Keyboard.Focus(_findField);
                _findField.SelectAll();
            }), DispatcherPriority.Input);
        }
    }

    public void HideFind()
    {
        _findBar.Visibility = Visibility.Collapsed;
        _findField.Text = "";
        FocusText();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_controller.HandleNoteKey(e)) e.Handled = true;
        else _controller.NoteActivity();
    }

    // MARK: Autosave — a second after typing stops

    /// A save is what makes the deck redraw and the tab labels catch up, so it waits
    /// for typing to actually stop rather than interrupting it four times a second.
    private void ScheduleSave(string value)
    {
        _save?.Stop();
        _save = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _save.Tick += (_, _) =>
        {
            _save?.Stop();
            _save = null;
            NoteStore.Shared.UpdateBody(_note.Id, value);
            _savedAt = DateTime.Now;
            UpdateSavedLabel();
        };
        _save.Start();
    }

    private void UpdateSavedLabel()
    {
        _savedLabelTimer.Stop();
        if (_savedAt is not { } savedAt)
        {
            _savedLabel.Text = "Not saved";
            return;
        }

        _savedLabel.Text = $"Saved · {Fmt.Ago(savedAt)}";
        if (!IsLoaded) return;

        var age = DateTime.Now - savedAt;
        TimeSpan? nextBoundary = age.TotalSeconds switch
        {
            < 60 => TimeSpan.FromSeconds(60),
            _ when age.TotalMinutes < 60 =>
                TimeSpan.FromMinutes(Math.Floor(age.TotalMinutes) + 1),
            _ when age.TotalHours < 24 =>
                TimeSpan.FromHours(Math.Floor(age.TotalHours) + 1),
            _ when age.TotalDays < 7 =>
                TimeSpan.FromDays(Math.Floor(age.TotalDays) + 1),
            _ => null,
        };
        if (nextBoundary is not { } boundary) return;

        // A small margin avoids waking a fraction before Fmt.Ago crosses its floor.
        var delay = boundary - age + TimeSpan.FromMilliseconds(50);
        _savedLabelTimer.Interval = delay > TimeSpan.FromMilliseconds(250)
            ? delay
            : TimeSpan.FromMilliseconds(250);
        _savedLabelTimer.Start();
    }

    public void Flush()
    {
        _save?.Stop();
        _save = null;
        NoteStore.Shared.UpdateBody(_note.Id, _text.PlainText);
    }
}
