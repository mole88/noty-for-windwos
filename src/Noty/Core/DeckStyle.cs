namespace Noty.Core;

/// How the deck draws itself once it fans out.
public enum DeckStyle
{
    /// Labelled vertical tabs — the full deck.
    Tabs,
    /// Colour chips only — barely touches the screen.
    Compact,
}

public static class DeckStyleNames
{
    public static string Title(this DeckStyle s) => s switch
    {
        DeckStyle.Tabs => "Labelled tabs",
        _ => "Colour chips",
    };
}
