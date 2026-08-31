namespace Noty.Deck;

public enum DeckPhase
{
    Rest,
    Fan,
    Expanded,
}

/// Rest / Fan / Expanded(noteId), with a rank so the controller can tell whether a
/// transition is opening up or winding down.
public readonly record struct DeckState(DeckPhase Phase, string? NoteId = null)
{
    public static readonly DeckState Rest = new(DeckPhase.Rest);
    public static readonly DeckState Fan = new(DeckPhase.Fan);
    public static DeckState Expanded(string id) => new(DeckPhase.Expanded, id);

    public int Rank => (int)Phase;
    public string? ExpandedId => Phase == DeckPhase.Expanded ? NoteId : null;
    public override string ToString() =>
        Phase == DeckPhase.Expanded ? $"expanded({NoteId})" : Phase.ToString().ToLowerInvariant();
}
