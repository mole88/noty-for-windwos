using System.Windows;

namespace Noty.Interop;

/// One physical display: bounds in device pixels, its work area, and its scale.
/// WPF lays out in DIPs, so everything the deck computes is multiplied through
/// `Scale` on the way to `SetWindowPos`.
public sealed record ScreenInfo(
    IntPtr Handle,
    string Device,
    Win32.RECT Bounds,
    Win32.RECT Work,
    double Scale)
{
    /// The display's work area in DIPs, which is what the deck lays out against.
    public Rect WorkDips => new(Work.Left / Scale, Work.Top / Scale,
                                Work.Width / Scale, Work.Height / Scale);

    public Rect BoundsDips => new(Bounds.Left / Scale, Bounds.Top / Scale,
                                  Bounds.Width / Scale, Bounds.Height / Scale);
}

public static class Screens
{
    public static List<ScreenInfo> All()
    {
        var list = new List<ScreenInfo>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Win32.RECT _, IntPtr _) =>
        {
            var info = new Win32.MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFOEX>(),
            };
            if (!Win32.GetMonitorInfo(h, ref info)) return true;

            double scale = 1.0;
            if (Win32.GetDpiForMonitor(h, Win32.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
                scale = dpiX / 96.0;

            list.Add(new ScreenInfo(h, info.szDevice, info.rcMonitor, info.rcWork, scale));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static Win32.POINT Cursor
    {
        get
        {
            Win32.GetCursorPos(out var p);
            return p;
        }
    }

    public static ScreenInfo? At(Win32.POINT p) =>
        All().FirstOrDefault(s => s.Bounds.Contains(p));
}
