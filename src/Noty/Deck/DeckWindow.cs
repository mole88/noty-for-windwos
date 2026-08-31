using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Noty.Core;
using Noty.Interop;

namespace Noty.Deck;

/// Borderless, click-through-where-blank, never-in-the-taskbar window that the deck
/// is drawn into.
///
/// It carries WS_EX_NOACTIVATE while dormant so brushing the pill cannot steal focus
/// from whatever you are working in. That bit has to come *off* before an open note
/// can take keystrokes — the Windows counterpart of overriding `canBecomeKey`.
public sealed class DeckWindow : Window
{
    /// A null background — not a transparent brush — is what makes the empty part of
    /// the window click-through. The window stays at full deck size in every state
    /// (resizing it on each transition made the pill flash), so most of it is empty
    /// most of the time and must never swallow a click meant for the app underneath.
    public Canvas Root { get; } = new()
    {
        Background = null,
        ClipToBounds = false,
    };

    private IntPtr _hwnd;
    private bool _acceptsKeys;

    public DeckWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = null;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Content = Root;
        // Placement is done in device pixels by hand; WPF must not second-guess it.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Width = 1;
        Height = 1;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        ApplyExStyle();
    }

    private void ApplyExStyle()
    {
        if (_hwnd == IntPtr.Zero) return;
        var ex = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
        ex |= Win32.WS_EX_TOOLWINDOW;        // never in the taskbar or Alt-Tab
        if (_acceptsKeys) ex &= ~Win32.WS_EX_NOACTIVATE;
        else ex |= Win32.WS_EX_NOACTIVATE;
        Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, ex);
    }

    /// An open note needs the keyboard; the pill and the fan must never take it.
    public void SetAcceptsKeys(bool value)
    {
        if (_acceptsKeys == value) return;
        _acceptsKeys = value;
        ApplyExStyle();
    }

    public void Raise()
    {
        if (_hwnd == IntPtr.Zero) return;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }

    public void Focus(bool foreground)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (foreground) Win32.SetForegroundWindow(_hwnd);
        Activate();
    }

    /// Place the window in device pixels. Everything the deck computes is in DIPs
    /// and gets multiplied through the monitor's scale on the way here, because a
    /// second display can be at a different DPI entirely.
    public void PlaceDevice(int x, int y, int w, int h)
    {
        if (_hwnd == IntPtr.Zero) return;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, w, h,
            Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }
}
