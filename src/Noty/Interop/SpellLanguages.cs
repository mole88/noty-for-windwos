using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Markup;
using Noty.Core;

namespace Noty.Interop;

/// Which languages Windows can actually spell-check.
///
/// WPF leaves a text view's `Language` at the framework default whatever you type
/// into it, so Russian text gets checked against an English dictionary and every
/// word comes back wrong. Following the keyboard layout fixes that — but only for
/// languages the machine has a dictionary for, and this is the same factory WPF
/// asks internally.
public static class SpellLanguages
{
    private static readonly Dictionary<string, bool> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static ISpellCheckerFactory? _factory;
    private static bool _factoryTried;

    /// The writing system a stretch of text is in. Enough to pick a dictionary —
    /// Noty is not trying to tell Russian from Ukrainian.
    public enum Script
    {
        Neutral,
        Latin,
        Cyrillic,
    }

    private static readonly string[] LatinTags = { "en-US", "en-GB", "de-DE", "fr-FR", "es-ES" };
    private static readonly string[] CyrillicTags = { "ru-RU", "uk-UA", "bg-BG", "be-BY" };

    private static readonly Dictionary<Script, XmlLanguage> ByScript = new();

    /// The language to spell-check a run of this script against.
    ///
    /// Picked from what Windows actually has a dictionary for, preferring the user's
    /// own display language when it is written in that script. If nothing matches,
    /// the canonical tag is used anyway: an unsupported language is left unchecked,
    /// which is the right outcome — no marks beats every word marked wrong.
    public static XmlLanguage ForScript(Script script)
    {
        if (ByScript.TryGetValue(script, out var cached)) return cached;

        var candidates = script == Script.Cyrillic ? CyrillicTags : LatinTags;
        var ui = CultureInfo.CurrentUICulture.IetfLanguageTag;
        var preferred = ScriptOf(CultureInfo.CurrentUICulture.NativeName) == script ? ui : null;

        var tag = preferred is not null && Supported(preferred)
            ? preferred
            : candidates.FirstOrDefault(Supported) ?? candidates[0];

        var language = XmlLanguage.GetLanguage(tag);
        ByScript[script] = language;
        Log.Line($"spellcheck dictionary for {script}: {tag}");
        return language;
    }

    /// The script one character belongs to; anything else — digits, punctuation,
    /// spaces — belongs to whatever it sits among.
    public static Script ScriptOf(char c)
    {
        if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') return Script.Latin;
        if (c is >= 'Ѐ' and <= 'ԯ') return Script.Cyrillic;
        if (c < 0x80) return Script.Neutral;
        return char.IsLetter(c) ? Script.Latin : Script.Neutral;
    }

    private static Script ScriptOf(string text)
    {
        foreach (var c in text)
        {
            var s = ScriptOf(c);
            if (s != Script.Neutral) return s;
        }
        return Script.Neutral;
    }

    public static bool Supported(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return false;
        if (Cache.TryGetValue(tag, out var known)) return known;

        var supported = Ask(tag);
        Cache[tag] = supported;
        Log.Line($"spellcheck for {tag}: {(supported ? "available" : "no dictionary")}");
        return supported;
    }

    private static bool Ask(string tag)
    {
        try
        {
            var factory = Factory();
            if (factory is null)
            {
                // No factory at all: fall back to the four WPF has always shipped.
                var lang = tag.Split('-')[0].ToLowerInvariant();
                return lang is "en" or "de" or "fr" or "es";
            }
            factory.IsSupported(tag, out var value);
            return value != 0;
        }
        catch (Exception e)
        {
            Log.Line($"spellcheck probe failed for {tag} — {e.Message}");
            return false;
        }
    }

    private static ISpellCheckerFactory? Factory()
    {
        if (_factoryTried) return _factory;
        _factoryTried = true;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC"));
            if (type is not null)
                _factory = Activator.CreateInstance(type) as ISpellCheckerFactory;
        }
        catch (Exception e)
        {
            Log.Line($"no spell checker factory — {e.Message}");
        }
        return _factory;
    }

    /// Only the first two slots are used, but every method has to be declared or the
    /// vtable slots do not line up.
    [ComImport]
    [Guid("8E018A9D-2415-4677-BF08-794EA61F94BB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        void GetSupportedLanguages(out IntPtr value);
        void IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out int value);
        void CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out IntPtr value);
        void RegisterUserDictionary([MarshalAs(UnmanagedType.LPWStr)] string dictionaryPath,
                                    [MarshalAs(UnmanagedType.LPWStr)] string languageTag);
        void UnregisterUserDictionary([MarshalAs(UnmanagedType.LPWStr)] string dictionaryPath,
                                      [MarshalAs(UnmanagedType.LPWStr)] string languageTag);
    }
}
