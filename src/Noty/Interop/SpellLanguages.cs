using System.Runtime.InteropServices;
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
