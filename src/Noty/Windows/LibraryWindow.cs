using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Noty.Core;

namespace Noty.Windows;

/// All Notes / Archive: one window, search across every note body and title, with an
/// editable detail pane.
public sealed class LibraryWindow : Window
{
    private readonly ListBox _list = new();
    private readonly TextBox _search = new();
    private readonly TextBox _detail = new();
    private readonly TextBlock _detailTitle = new();
    private readonly TextBlock _detailMeta = new();
    private readonly Border _detailPane;
    private readonly TabControl _tabs = new();
    private Button? _archiveButton;

    private DispatcherTimer? _save;
    private string? _selectedId;
    private bool _loading;

    private bool ShowingArchive => _tabs.SelectedIndex == 1;

    public LibraryWindow()
    {
        Title = "Noty — All Notes";
        Width = 900;
        Height = 560;
        MinWidth = 620;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22));
        Foreground = Brushes.White;
        FontFamily = Ink.SystemFace;
        Theme.Apply(this);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        root.Children.Add(BuildToolbar());

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(split, 1);

        _list.Background = Brushes.Transparent;
        _list.BorderThickness = new Thickness(0);
        _list.Margin = new Thickness(8, 0, 8, 8);
        _list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _list.SelectionChanged += (_, _) => LoadSelection();
        split.Children.Add(_list);

        _detailPane = BuildDetail();
        Grid.SetColumn(_detailPane, 1);
        split.Children.Add(_detailPane);

        root.Children.Add(split);
        Content = root;

        NoteStore.Shared.NotesChanged += OnNotesChanged;
        Closed += (_, _) =>
        {
            Flush();
            NoteStore.Shared.NotesChanged -= OnNotesChanged;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                NewNote();
                e.Handled = true;
            }
        };

        Reload();
    }

    private void OnNotesChanged(object? s, EventArgs e) => Dispatcher.BeginInvoke(new Action(Reload));

    public void ShowArchive(bool archived)
    {
        _tabs.SelectedIndex = archived ? 1 : 0;
        Title = archived ? "Noty — Archive" : "Noty — All Notes";
    }

    private FrameworkElement BuildToolbar()
    {
        var bar = new Grid { Margin = new Thickness(12, 10, 12, 8) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _tabs.Items.Add(new TabItem { Header = "All Notes" });
        _tabs.Items.Add(new TabItem { Header = "Archive" });
        _tabs.SelectedIndex = 0;
        _tabs.Background = Brushes.Transparent;
        _tabs.BorderThickness = new Thickness(0);
        _tabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, _tabs)) return;
            Title = ShowingArchive ? "Noty — Archive" : "Noty — All Notes";
            Reload();
        };
        bar.Children.Add(_tabs);

        // The one thing the window could not do. Amber, because it is the only
        // action here that makes something rather than acting on what is selected.
        var add = new Button
        {
            Content = "＋  New note",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(14, 6, 14, 6),
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xAD, 0x08)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0x1F, 0x05)),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "New note  Ctrl+N",
        };
        add.Click += (_, _) => NewNote();
        Grid.SetColumn(add, 1);
        bar.Children.Add(add);

        var search = SearchBox();
        Grid.SetColumn(search, 2);
        bar.Children.Add(search);
        return bar;
    }

    /// Make a note and drop the caret straight into it.
    ///
    /// The selection is moved by picking the row, not by writing the id: switching
    /// it by hand would leave the detail pane's text belonging to the previous note
    /// while the id already pointed at the new one, and the next save would put one
    /// into the other.
    private void NewNote()
    {
        // A new note is not archived, so it would land in a list that is not showing.
        if (ShowingArchive) _tabs.SelectedIndex = 0;
        // Nor would it survive a filter it does not match.
        if (_search.Text.Length > 0) _search.Clear();

        var note = NoteStore.Shared.Create();
        Reload();
        SelectById(note.Id);
        _detail.Focus();
    }

    private void SelectById(string id)
    {
        var row = _list.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string?)i.Tag == id);
        if (row is null) return;
        _list.SelectedItem = row;
        _list.ScrollIntoView(row);
    }

    /// A field that looks like one: a magnifier, a hint while it is empty, and a
    /// border you can see. On its own the box read as a dark rectangle with nothing
    /// to say it took typing.
    private FrameworkElement SearchBox()
    {
        _search.Background = Brushes.Transparent;
        _search.BorderThickness = new Thickness(0);
        _search.Padding = new Thickness(0);
        _search.VerticalAlignment = VerticalAlignment.Center;
        _search.VerticalContentAlignment = VerticalAlignment.Center;

        var hint = new TextBlock
        {
            Text = "Search notes",
            FontSize = 12.5,
            Foreground = NoteColor.Tint(Colors.White, 0.35),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };

        _search.TextChanged += (_, _) =>
        {
            hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Reload();
        };

        var field = new Grid();
        field.Children.Add(hint);
        field.Children.Add(_search);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(new TextBlock
        {
            Text = "🔍",
            FontSize = 11,
            Foreground = NoteColor.Tint(Colors.White, 0.45),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        });
        Grid.SetColumn(field, 1);
        row.Children.Add(field);

        var clear = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(6, 2, 6, 2),
            Background = Brushes.Transparent,
            Foreground = NoteColor.Tint(Colors.White, 0.5),
            Margin = new Thickness(6, 0, -4, 0),
            Focusable = false,
            ToolTip = "Clear",
        };
        clear.Click += (_, _) =>
        {
            _search.Clear();
            _search.Focus();
        };
        clear.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Text.Length")
        {
            Source = _search,
            Converter = new CountToVisibility(),
        });
        Grid.SetColumn(clear, 2);
        row.Children.Add(clear);

        var box = new Border
        {
            Width = 280,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x31)),
            BorderBrush = NoteColor.Tint(Colors.White, 0.15),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 0, 9, 0),
            Child = row,
        };
        return box;
    }

    /// Shows the clear button only once there is something to clear.
    private sealed class CountToVisibility : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c) =>
            value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c) =>
            throw new NotSupportedException();
    }

    private Border BuildDetail()
    {
        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _detailTitle.FontSize = 15;
        _detailTitle.FontWeight = FontWeights.SemiBold;
        _detailTitle.Foreground = Brushes.White;
        _detailTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        grid.Children.Add(_detailTitle);

        _detailMeta.FontSize = 11;
        _detailMeta.Foreground = NoteColor.Tint(Colors.White, 0.45);
        _detailMeta.Margin = new Thickness(0, 3, 0, 8);
        Grid.SetRow(_detailMeta, 1);
        grid.Children.Add(_detailMeta);

        _detail.AcceptsReturn = true;
        _detail.AcceptsTab = true;
        _detail.TextWrapping = TextWrapping.Wrap;
        _detail.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _detail.Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B));
        _detail.Foreground = Brushes.White;
        _detail.CaretBrush = Brushes.White;
        _detail.BorderThickness = new Thickness(0);
        _detail.Padding = new Thickness(10);
        _detail.FontFamily = Ink.BodyFamily;
        _detail.FontSize = Ink.BodySize(Settings.NoteFontSize);
        _detail.TextChanged += (_, _) => ScheduleSave();
        Grid.SetRow(_detail, 2);
        grid.Children.Add(_detail);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(Action("Cycle colour", () =>
        {
            if (_selectedId is { } id) NoteStore.Shared.CycleColor(id);
        }));
        // One button, but it says which of the two things it is about to do.
        _archiveButton = Action("Archive", () =>
        {
            if (_selectedId is not { } id) return;
            var note = NoteStore.Shared.Get(id);
            if (note is null) return;
            NoteStore.Shared.SetArchived(id, !note.Archived);
        });
        buttons.Children.Add(_archiveButton);
        buttons.Children.Add(Action("Delete", () =>
        {
            if (_selectedId is { } id) NoteStore.Shared.Delete(id);
        }));
        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E)),
            Child = grid,
        };
    }

    private static Button Action(string title, Action action)
    {
        var b = new Button
        {
            Content = title,
            Margin = new Thickness(6, 0, 0, 0),
        };
        b.Click += (_, _) => action();
        return b;
    }

    // MARK: Data

    private void Reload()
    {
        var q = _search.Text.Trim();
        var source = ShowingArchive ? NoteStore.Shared.Archived : NoteStore.Shared.Active;
        if (q.Length > 0)
        {
            source = source.Where(n =>
                n.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                n.Body.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        }

        var keep = _selectedId;
        _loading = true;
        _list.Items.Clear();
        foreach (var n in source) _list.Items.Add(Row(n));
        _loading = false;

        var match = _list.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string?)i.Tag == keep);
        if (match is not null) _list.SelectedItem = match;
        else if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        else ClearDetail();
    }

    private ListBoxItem Row(Note n)
    {
        var grid = new Grid { Margin = new Thickness(2) };
        // The bar is 4 wide and the gap after it is 8, so the column has to hold
        // both: a 4-wide column with an 8-wide margin inside it leaves the bar
        // nothing to draw in, and the only trace of a note's colour vanished.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new Border
        {
            Background = n.Palette.DashBrush,
            CornerRadius = new CornerRadius(2),
            Width = 4,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 1, 0, 1),
        });

        var stack = new StackPanel();
        Grid.SetColumn(stack, 1);
        stack.Children.Add(new TextBlock
        {
            Text = n.DisplayTitle,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var meta = n.TaskProgress is { } p ? $"{p.Done}/{p.Total} done · " : "";
        stack.Children.Add(new TextBlock
        {
            Text = meta + Fmt.Ago(n.Modified),
            FontSize = 10.5,
            Foreground = NoteColor.Tint(Colors.White, 0.45),
        });

        if (n.Preview.Length > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = n.Preview,
                FontSize = 11,
                Foreground = NoteColor.Tint(Colors.White, 0.6),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }
        grid.Children.Add(stack);

        return new ListBoxItem
        {
            Content = grid,
            Tag = n.Id,
            Padding = new Thickness(6),
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
        };
    }

    private void LoadSelection()
    {
        if (_loading) return;
        Flush();
        if (_list.SelectedItem is not ListBoxItem item || item.Tag is not string id)
        {
            ClearDetail();
            return;
        }
        var note = NoteStore.Shared.Get(id);
        if (note is null)
        {
            ClearDetail();
            return;
        }
        _selectedId = id;
        _detailTitle.Text = note.DisplayTitle;
        if (_archiveButton is not null) _archiveButton.Content = note.Archived ? "Restore" : "Archive";
        _detailMeta.Text = $"{note.Palette.Name} · created {Fmt.Stamp(note.Created)} · " +
                           $"modified {Fmt.Stamp(note.Modified)}" +
                           (note.Archived ? " · archived" : "") +
                           (note.Pinned ? " · pinned" : "");
        _loading = true;
        _detail.Text = note.Body;
        _loading = false;
        _detailPane.Background = new SolidColorBrush(
            Color.FromArgb(0x22, note.Palette.Dash.R, note.Palette.Dash.G, note.Palette.Dash.B));
    }

    private void ClearDetail()
    {
        _selectedId = null;
        _detailTitle.Text = "No note selected";
        _detailMeta.Text = "";
        _loading = true;
        _detail.Text = "";
        _loading = false;
    }

    private void ScheduleSave()
    {
        if (_loading || _selectedId is null) return;
        _save?.Stop();
        _save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _save.Tick += (_, _) =>
        {
            _save?.Stop();
            _save = null;
            Flush();
        };
        _save.Start();
    }

    private void Flush()
    {
        _save?.Stop();
        _save = null;
        if (_selectedId is { } id) NoteStore.Shared.UpdateBody(id, _detail.Text);
    }
}
