using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Noty.Core;

/// The handful of togglable preferences, in one JSON file beside the database.
/// Writes are debounced through Save(), which every setter calls.
public static class Settings
{
    private sealed class Model
    {
        public bool ShowOverFullScreen { get; set; }
        public bool DeckOnLeftEdge { get; set; }
        public double NoteFontSize { get; set; } = 13.5;
        public string NoteFontName { get; set; } = "Segoe Script";
        public double EdgeWidth { get; set; } = 14;
        public bool MarkdownStyling { get; set; } = true;
        public DeckStyle DeckStyle { get; set; } = DeckStyle.Tabs;

        public Shortcut ScNewNote { get; set; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.N);
        public Shortcut ScAllNotes { get; set; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.A);
        public Shortcut ScArchive { get; set; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.L);

        public Shortcut ScArchiveNote { get; set; } = new(ModifierKeys.Control | ModifierKeys.Shift, Key.A);
        public Shortcut ScClose { get; set; } = new(ModifierKeys.None, Key.Escape);
        public Shortcut ScFind { get; set; } = new(ModifierKeys.Control, Key.F);
        public Shortcut ScTask { get; set; } = new(ModifierKeys.Control, Key.T);
        public Shortcut ScPin { get; set; } = new(ModifierKeys.Control, Key.P);
        public Shortcut ScColour { get; set; } = new(ModifierKeys.Control, Key.OemPeriod);
        // Not plain Ctrl+Backspace: that belongs to the text view, where it deletes
        // the word before the caret the way it does in every other editor.
        public Shortcut ScDelete { get; set; } = new(ModifierKeys.Control | ModifierKeys.Shift, Key.Back);
        public Shortcut ScBigger { get; set; } = new(ModifierKeys.Control, Key.OemPlus);
        public Shortcut ScSmaller { get; set; } = new(ModifierKeys.Control, Key.OemMinus);
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Model M = Load();

    private static Model Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
            {
                var m = JsonSerializer.Deserialize<Model>(File.ReadAllText(Paths.SettingsFile), Json);
                if (m is not null) return Migrate(m);
            }
        }
        catch (Exception e)
        {
            Log.Line($"settings load failed — {e.Message}");
        }
        return new Model();
    }

    /// Settings written by an older build are brought forward here.
    private static Model Migrate(Model m)
    {
        // Ctrl+Backspace used to delete the note. It is the word-delete key in every
        // text field on Windows, so anyone still holding the old binding is moved to
        // Ctrl+Shift+Backspace rather than losing a word and a note at once.
        if (m.ScDelete.Modifiers == ModifierKeys.Control && m.ScDelete.Key == Key.Back)
        {
            m.ScDelete = new Shortcut(ModifierKeys.Control | ModifierKeys.Shift, Key.Back);
            Log.Line("migrated settings — delete-note moved off Ctrl+Backspace");
        }
        return m;
    }

    private static DispatcherTimer? _writeBack;

    /// Every setter calls this, and a slider calls its setter on every pixel of the
    /// drag — so the write itself waits for the value to settle. Flush() forces it
    /// out when the app is closing.
    public static void Save()
    {
        _writeBack?.Stop();
        _writeBack = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _writeBack.Tick += (_, _) => Flush();
        _writeBack.Start();
    }

    public static void Flush()
    {
        _writeBack?.Stop();
        _writeBack = null;
        try
        {
            File.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(M, Json));
        }
        catch (Exception e)
        {
            Log.Line($"settings save failed — {e.Message}");
        }
    }

    public static bool ShowOverFullScreen
    {
        get => M.ShowOverFullScreen;
        set { M.ShowOverFullScreen = value; Save(); }
    }

    public static bool DeckOnLeftEdge
    {
        get => M.DeckOnLeftEdge;
        set { M.DeckOnLeftEdge = value; Save(); }
    }

    /// Max tabs the fan shows before collapsing the remainder into "+N".
    /// Five keeps every tab at full size instead of squeezing the deck.
    public const int FanLimit = 5;

    /// Body text size inside a note.
    public static readonly (string Name, double Size)[] FontSizes =
    {
        ("Small", 12), ("Medium", 13.5), ("Large", 15.5), ("Extra Large", 18),
    };

    public const double FontMin = 10, FontMax = 30;

    public static double NoteFontSize
    {
        get => M.NoteFontSize is >= FontMin and <= FontMax ? M.NoteFontSize : 13.5;
        set { M.NoteFontSize = Math.Clamp(value, FontMin, FontMax); Save(); }
    }

    /// Family the note body is set in; empty means the system font. Defaults to a
    /// hand, the way a sticky note actually looks.
    public static string NoteFontName
    {
        get => M.NoteFontName;
        set { M.NoteFontName = value; Save(); }
    }

    /// How far from the screen edge the deck notices the pointer. A wider strip is
    /// easier to hit; a narrower one stays further out of the way.
    public static readonly (string Name, double Width)[] EdgeWidths =
    {
        ("Narrow", 8), ("Standard", 14), ("Wide", 28), ("Very wide", 44),
    };

    public static double EdgeWidth
    {
        get => M.EdgeWidth >= 4 ? M.EdgeWidth : 14;
        set { M.EdgeWidth = value; Save(); }
    }

    /// Style Markdown inline — headings, emphasis, code, quotes.
    public static bool MarkdownStyling
    {
        get => M.MarkdownStyling;
        set { M.MarkdownStyling = value; Save(); }
    }

    /// How long the deck may sit untouched before it tidies itself away.
    public static readonly TimeSpan FanIdleTimeout = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan NoteIdleTimeout = TimeSpan.FromSeconds(60);

    public static DeckStyle DeckStyle
    {
        get => M.DeckStyle;
        set { M.DeckStyle = value; Save(); }
    }

    // MARK: Shortcuts

    public static Shortcut ScNewNote { get => M.ScNewNote; set { M.ScNewNote = value; Save(); } }
    public static Shortcut ScAllNotes { get => M.ScAllNotes; set { M.ScAllNotes = value; Save(); } }
    public static Shortcut ScArchive { get => M.ScArchive; set { M.ScArchive = value; Save(); } }

    public static Shortcut ScArchiveNote { get => M.ScArchiveNote; set { M.ScArchiveNote = value; Save(); } }
    public static Shortcut ScClose { get => M.ScClose; set { M.ScClose = value; Save(); } }
    public static Shortcut ScFind { get => M.ScFind; set { M.ScFind = value; Save(); } }
    public static Shortcut ScTask { get => M.ScTask; set { M.ScTask = value; Save(); } }
    public static Shortcut ScPin { get => M.ScPin; set { M.ScPin = value; Save(); } }
    public static Shortcut ScColour { get => M.ScColour; set { M.ScColour = value; Save(); } }
    public static Shortcut ScDelete { get => M.ScDelete; set { M.ScDelete = value; Save(); } }
    public static Shortcut ScBigger { get => M.ScBigger; set { M.ScBigger = value; Save(); } }
    public static Shortcut ScSmaller { get => M.ScSmaller; set { M.ScSmaller = value; Save(); } }

    // MARK: Launch at login — HKCU Run, no elevation needed

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Noty";

    public static bool LaunchAtLogin
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunValue) is string s && s.Length > 0;
            }
            catch { return false; }
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (key is null) return;
                if (value)
                {
                    var exe = Environment.ProcessPath;
                    if (exe is null) return;
                    key.SetValue(RunValue, $"\"{exe}\"");
                }
                else key.DeleteValue(RunValue, throwOnMissingValue: false);
            }
            catch (Exception e)
            {
                Log.Line($"launch-at-login toggle failed — {e.Message}");
            }
        }
    }
}
