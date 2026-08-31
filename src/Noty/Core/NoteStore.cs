using System.ComponentModel;
using System.Windows.Threading;

namespace Noty.Core;

/// Observable in-memory model. SQLite is written through on every mutation; the
/// list is the single source of truth for every window and deck.
public sealed class NoteStore : INotifyPropertyChanged
{
    public static NoteStore Shared { get; } = new();

    private readonly Store _store = new();
    private readonly List<Note> _notes;
    private DispatcherTimer? _undoTimer;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// Raised whenever the note list itself changed — the deck and every window
    /// redraw off this.
    public event EventHandler? NotesChanged;

    public sealed record PendingDelete(Note Note, DateTime Deadline);

    private PendingDelete? _pendingUndo;

    /// Set when a note is deleted, cleared after the ten-second undo window.
    public PendingDelete? PendingUndo
    {
        get => _pendingUndo;
        private set
        {
            _pendingUndo = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingUndo)));
            PendingUndoChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? PendingUndoChanged;

    private NoteStore()
    {
        _notes = _store.Load();
        if (_notes.Count == 0) SeedWelcomeNote();
    }

    // MARK: Derived collections

    public IReadOnlyList<Note> Notes => _notes;

    public List<Note> Active =>
        _notes.Where(n => !n.Archived).OrderBy(n => n.Order).ToList();

    public List<Note> Archived =>
        _notes.Where(n => n.Archived).OrderByDescending(n => n.Modified).ToList();

    public Note? Get(string id) => _notes.FirstOrDefault(n => n.Id == id);

    private void Changed() => NotesChanged?.Invoke(this, EventArgs.Empty);

    // MARK: Mutations

    public Note Create(string body = "", int? color = null)
    {
        var active = Active;
        var n = new Note
        {
            // newest sits at the top of the deck
            Order = (active.Count > 0 ? active.Min(x => x.Order) : 0) - 1,
            Color = color ?? _notes.Count % NoteColor.All.Count,
            Body = body,
        };
        n.Title = Note.DerivedTitle(body);
        _notes.Add(n);
        _store.Upsert(n);
        Changed();
        return n;
    }

    public void UpdateBody(string id, string body)
    {
        var n = Get(id);
        if (n is null || n.Body == body) return;
        n.Body = body;
        n.Title = Note.DerivedTitle(body);
        n.Modified = DateTime.Now;
        _store.Upsert(n);
        Changed();
    }

    public void TogglePin(string id)
    {
        var n = Get(id);
        if (n is null) return;
        n.Pinned = !n.Pinned;
        _store.Upsert(n);
        Changed();
    }

    public void CycleColor(string id)
    {
        var n = Get(id);
        if (n is null) return;
        n.Color = (n.Color + 1) % NoteColor.All.Count;
        n.Modified = DateTime.Now;
        _store.Upsert(n);
        Changed();
    }

    public void SetColor(string id, int color)
    {
        var n = Get(id);
        if (n is null) return;
        n.Color = color;
        n.Modified = DateTime.Now;
        _store.Upsert(n);
        Changed();
    }

    public void SetArchived(string id, bool archived)
    {
        var n = Get(id);
        if (n is null) return;
        n.Archived = archived;
        n.Modified = DateTime.Now;
        if (!archived)
        {
            var active = Active;
            n.Order = (active.Count > 0 ? active.Min(x => x.Order) : 0) - 1;
        }
        _store.Upsert(n);
        Changed();
    }

    /// Removes the note but keeps it recoverable for ten seconds.
    public void Delete(string id)
    {
        var n = Get(id);
        if (n is null) return;
        _notes.Remove(n);
        _store.Delete(id);
        Changed();

        PendingUndo = new PendingDelete(n, DateTime.Now.AddSeconds(10));
        _undoTimer?.Stop();
        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _undoTimer.Tick += (_, _) =>
        {
            _undoTimer?.Stop();
            PendingUndo = null;
        };
        _undoTimer.Start();
    }

    /// Permanently remove a just-created draft that never acquired any content.
    /// This is deliberately separate from Delete: silently abandoning an empty
    /// draft should not replace a real pending deletion in the Undo toast.
    public bool DiscardIfEmpty(string id)
    {
        var n = Get(id);
        if (n is null || !string.IsNullOrWhiteSpace(n.Body)) return false;
        _notes.Remove(n);
        _store.Delete(id);
        Changed();
        return true;
    }

    public void UndoDelete()
    {
        if (PendingUndo is not { } p) return;
        _undoTimer?.Stop();
        _notes.Add(p.Note);
        _store.Upsert(p.Note);
        PendingUndo = null;
        Changed();
    }

    /// Move a note `slots` positions up or down the deck, rewriting the order column
    /// densely so repeated drags cannot drift the values apart.
    public void Reorder(string id, int slots)
    {
        if (slots == 0) return;
        var list = Active;
        var from = list.FindIndex(n => n.Id == id);
        if (from < 0) return;
        var to = Math.Clamp(from + slots, 0, list.Count - 1);
        if (to == from) return;

        var moved = list[from];
        list.RemoveAt(from);
        list.Insert(to, moved);
        for (var rank = 0; rank < list.Count; rank++)
        {
            var n = list[rank];
            if (Math.Abs(n.Order - rank) < double.Epsilon) continue;
            n.Order = rank;
            _store.Upsert(n);
        }
        Changed();
    }

    public void Move(string id, string? beforeId)
    {
        var n = Get(id);
        if (n is null) return;
        var list = Active;
        double newOrder;
        var target = beforeId is null ? -1 : list.FindIndex(x => x.Id == beforeId);
        if (target >= 0)
        {
            var upper = list[target].Order;
            var lower = target > 0 ? list[target - 1].Order : upper - 2;
            newOrder = (upper + lower) / 2;
        }
        else newOrder = (list.Count > 0 ? list.Max(x => x.Order) : 0) + 1;
        n.Order = newOrder;
        _store.Upsert(n);
        Changed();
    }

    /// Bulk insert used by import — returns how many notes landed.
    public int Ingest(IEnumerable<Note> incoming)
    {
        var added = 0;
        var b = (_notes.Count > 0 ? _notes.Min(x => x.Order) : 0) - 1;
        foreach (var n in incoming)
        {
            if (_notes.Any(x => x.Id == n.Id)) n.Id = Guid.NewGuid().ToString();
            n.Order = b;
            b -= 1;
            _notes.Add(n);
            _store.Upsert(n);
            added++;
        }
        if (added > 0) Changed();
        return added;
    }

    private void SeedWelcomeNote()
    {
        Create("""
        Welcome to Noty

        Your notes live at the edge of the screen. Slide the pointer to the right edge and the deck fans out.

        Ctrl+Alt+N  new note
        Ctrl+Alt+A  all notes
        Ctrl+Alt+L  archive

        Inside a note: Esc closes, Ctrl+F finds, Ctrl+. cycles the colour, Ctrl+Shift+Backspace deletes with ten seconds to undo.
        """, color: 0);
    }
}
