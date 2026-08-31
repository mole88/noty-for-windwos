using System.Text.Json.Serialization;
using System.Windows.Input;

namespace Noty.Core;

/// A key plus its modifiers, stored as text so settings.json stays readable.
public sealed class Shortcut : IEquatable<Shortcut>
{
    public Key Key { get; set; } = Key.None;
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.None;

    public Shortcut() { }

    public Shortcut(ModifierKeys modifiers, Key key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    [JsonIgnore]
    public bool IsSet => Key != Key.None;

    /// True when this WPF key event is the shortcut. `Key.System` arrives for any
    /// Alt combination, with the real key in SystemKey.
    public bool Matches(KeyEventArgs e)
    {
        if (!IsSet) return false;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return key == Key && Keyboard.Modifiers == Modifiers;
    }

    public override string ToString()
    {
        if (!IsSet) return "—";
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Pretty(Key));
        return string.Join("+", parts);
    }

    private static string Pretty(Key k) => k switch
    {
        Key.Back => "Backspace",
        Key.Escape => "Esc",
        Key.OemPeriod => ".",
        Key.OemComma => ",",
        Key.OemPlus => "+",
        Key.OemMinus => "−",
        Key.Add => "Num +",
        Key.Subtract => "Num −",
        _ => k.ToString(),
    };

    public bool Equals(Shortcut? other) =>
        other is not null && other.Key == Key && other.Modifiers == Modifiers;

    public override bool Equals(object? o) => Equals(o as Shortcut);
    public override int GetHashCode() => HashCode.Combine(Key, Modifiers);
}
