using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Noty.Core;
using Noty.Deck;
using Noty.Windows;

namespace Noty.Services;

/// Everything the pill's menu, the tray icon and the global shortcuts can do.
/// The original hangs these off AppDelegate; here they sit in one place both the
/// deck and the tray can reach.
public static class Actions
{
    public static DeckManager? Decks { get; set; }

    /// Raised only when a shortcut is actually rebound. Re-registering global hotkeys
    /// means letting go of them and taking them again, and a preference that has
    /// nothing to do with keys — the text-size slider, say — must not drag the whole
    /// set through that on every tick of the drag.
    public static Action? OnShortcutsChanged { get; set; }

    private static LibraryWindow? _library;
    private static SettingsWindow? _settings;

    public static void NewNote()
    {
        var note = NoteStore.Shared.Create();
        var deck = Decks?.Focused;
        deck?.Expand(note.Id, discardIfStillEmpty: true);
    }

    public static void OpenAllNotes() => ShowLibrary(archived: false);
    public static void OpenArchive() => ShowLibrary(archived: true);

    private static void ShowLibrary(bool archived)
    {
        if (_library is null || !_library.IsLoaded)
        {
            _library = new LibraryWindow();
            _library.Closed += (_, _) => _library = null;
        }
        _library.ShowArchive(archived);
        _library.Show();
        _library.Activate();
    }

    public static void OpenSettings()
    {
        if (_settings is null || !_settings.IsLoaded)
        {
            _settings = new SettingsWindow();
            _settings.Closed += (_, _) => _settings = null;
        }
        _settings.Show();
        _settings.Activate();
    }

    public static void Quit() => Application.Current.Shutdown();

    // MARK: Preferences — everything applies immediately

    public static void ToggleOverFullScreen()
    {
        Settings.ShowOverFullScreen = !Settings.ShowOverFullScreen;
        Refresh();
    }

    public static void SetDeckStyle(DeckStyle style)
    {
        Settings.DeckStyle = style;
        Refresh();
    }

    public static void SetNoteFont(string family)
    {
        Settings.NoteFontName = family;
        Refresh();
    }

    public static void SetFontSize(double size)
    {
        Settings.NoteFontSize = size;
        Refresh();
    }

    public static void StepFontSize(double by)
    {
        Settings.NoteFontSize = Math.Clamp(Settings.NoteFontSize + by, Settings.FontMin, Settings.FontMax);
        Refresh();
    }

    public static void ToggleDeckEdge()
    {
        Settings.DeckOnLeftEdge = !Settings.DeckOnLeftEdge;
        Refresh();
    }

    public static void ToggleLaunchAtLogin() => Settings.LaunchAtLogin = !Settings.LaunchAtLogin;

    private static DispatcherTimer? _refresh;

    /// Redraw every deck against the current preferences.
    ///
    /// Coalesced: a slider fires this on every pixel of its travel, and each call
    /// re-lays-out and repaints every deck on every display.
    public static void Refresh()
    {
        _refresh?.Stop();
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _refresh.Tick += (_, _) =>
        {
            _refresh?.Stop();
            _refresh = null;
            Decks?.RefreshAll();
        };
        _refresh.Start();
    }

    /// A shortcut was rebound: hand the whole set back and take it again.
    public static void ShortcutsChanged() => OnShortcutsChanged?.Invoke();

    // MARK: The pill's menu — also what the tray icon shows

    public static ContextMenu BuildMainMenu(DeckController? deck = null)
    {
        var menu = new ContextMenu();

        menu.Items.Add(Item($"New Note  {Settings.ScNewNote}", NewNote));
        menu.Items.Add(Item($"All Notes  {Settings.ScAllNotes}", OpenAllNotes));
        menu.Items.Add(Item($"Archive  {Settings.ScArchive}", OpenArchive));
        menu.Items.Add(new Separator());

        menu.Items.Add(Check("Show over full-screen apps", Settings.ShowOverFullScreen,
            ToggleOverFullScreen));

        var style = new MenuItem { Header = "Deck style" };
        foreach (DeckStyle s in Enum.GetValues<DeckStyle>())
        {
            var captured = s;
            style.Items.Add(Check(s.Title(), Settings.DeckStyle == s, () => SetDeckStyle(captured)));
        }
        menu.Items.Add(style);

        var font = new MenuItem { Header = "Note font" };
        foreach (var f in Ink.Faces)
        {
            var captured = f;
            font.Items.Add(Check(f.Name, Ink.Face.Family == f.Family,
                () => SetNoteFont(captured.Family)));
        }
        menu.Items.Add(font);

        var text = new MenuItem { Header = "Text size" };
        foreach (var (name, size) in Settings.FontSizes)
        {
            var captured = size;
            text.Items.Add(Check(name, Math.Abs(Settings.NoteFontSize - size) < 0.01,
                () => SetFontSize(captured)));
        }
        menu.Items.Add(text);

        menu.Items.Add(Check("Dock deck to left edge", Settings.DeckOnLeftEdge, ToggleDeckEdge));
        menu.Items.Add(new Separator());
        menu.Items.Add(Check("Launch at login", Settings.LaunchAtLogin, ToggleLaunchAtLogin));
        menu.Items.Add(new Separator());

        var export = new MenuItem { Header = "Export" };
        export.Items.Add(Item("Markdown (one file per note)…",
            () => Transfer.Export(Transfer.Format.Markdown, NoteStore.Shared.Notes.ToList())));
        export.Items.Add(Item("Plain text (one file per note)…",
            () => Transfer.Export(Transfer.Format.PlainText, NoteStore.Shared.Notes.ToList())));
        export.Items.Add(Item("Single document…",
            () => Transfer.Export(Transfer.Format.SingleFile, NoteStore.Shared.Notes.ToList())));
        export.Items.Add(Item("Sticky archive (.stickies)…",
            () => Transfer.Export(Transfer.Format.Stickies, NoteStore.Shared.Notes.ToList())));
        menu.Items.Add(export);
        menu.Items.Add(Item("Import…", Transfer.Import));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Settings…", OpenSettings));
        menu.Items.Add(Item("Quit Noty", Quit));

        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Check(string header, bool on, Action action)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = on };
        item.Click += (_, _) => action();
        return item;
    }
}
