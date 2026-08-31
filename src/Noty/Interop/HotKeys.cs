using System.Windows.Input;
using System.Windows.Interop;
using Noty.Core;

namespace Noty.Interop;

/// Global shortcuts through RegisterHotKey. No elevation, no accessibility
/// permission — the Windows counterpart of the original's Carbon hotkeys.
public sealed class HotKeys : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotKeys()
    {
        // A message-only window: never shown, exists purely to receive WM_HOTKEY.
        _source = new HwndSource(new HwndSourceParameters("NotyHotKeys")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3),   // HWND_MESSAGE
        });
        _source.AddHook(Hook);
    }

    /// Returns false when Windows refuses the binding. The caller owns the user-facing
    /// explanation because it knows which command the shortcut belongs to.
    public bool Register(Shortcut shortcut, Action action)
    {
        if (!shortcut.IsSet) return true;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(shortcut.Key);
        if (vk == 0)
        {
            Log.Line($"hotkey {shortcut} has no Windows virtual-key mapping");
            return false;
        }

        uint mods = Win32.MOD_NOREPEAT;
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Control)) mods |= Win32.MOD_CONTROL;
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= Win32.MOD_ALT;
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= Win32.MOD_SHIFT;
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= Win32.MOD_WIN;

        var id = _nextId++;
        if (Win32.RegisterHotKey(_source.Handle, id, mods, vk))
        {
            _actions[id] = action;
            return true;
        }

        var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        Log.Line($"hotkey {shortcut} could not be registered (Win32 error {error})");
        return false;
    }

    /// Called after the user rebinds a global shortcut in Settings.
    public void Clear()
    {
        foreach (var id in _actions.Keys) Win32.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
        _nextId = 1;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Win32.WM_HOTKEY) return IntPtr.Zero;
        if (_actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }
}
