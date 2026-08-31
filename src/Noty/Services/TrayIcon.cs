using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Interop;
using Noty.Core;
using Noty.Interop;

namespace Noty.Services;

/// Windows has no accessory-app dock trick to opt out of, but it does expect a way
/// back into an app with no window — so the pill's menu is also a tray icon.
/// The icon is drawn at startup rather than shipped as a file: a single sticky note
/// on its edge, which is the whole app.
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Window _menuOwner;
    private System.Windows.Controls.ContextMenu? _menu;

    public TrayIcon()
    {
        _menuOwner = new Window
        {
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Opacity = 0,
            Left = -32000,
            Top = -32000,
            Width = 1,
            Height = 1,
        };

        _icon = new NotifyIcon
        {
            Icon = Draw(),
            Text = "Noty",
            Visible = true,
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Actions.OpenAllNotes();
                return;
            }
            ShowMenu();
        };
    }

    private void ShowMenu()
    {
        if (_menu is not null) _menu.IsOpen = false;

        _menu = Actions.BuildMainMenu();
        _menu.Placement = PlacementMode.MousePoint;
        _menu.PlacementTarget = _menuOwner;
        _menu.StaysOpen = false;
        _menu.Closed += (_, _) => _menuOwner.Hide();

        _menuOwner.Show();
        var hwnd = new WindowInteropHelper(_menuOwner).Handle;
        Win32.SetForegroundWindow(hwnd);
        _menuOwner.Activate();
        _menu.IsOpen = true;
    }

    public static Icon Draw()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var paper = NoteColor.At(0).Paper;
            var dash = NoteColor.At(0).Dash;

            using var sheet = new SolidBrush(Color.FromArgb(255, paper.R, paper.G, paper.B));
            using var edge = new SolidBrush(Color.FromArgb(255, dash.R, dash.G, dash.B));

            g.FillRectangle(sheet, 6, 4, 20, 24);
            g.FillRectangle(edge, 22, 4, 4, 24);   // the coloured edge the deck shows

            using var ink = new Pen(Color.FromArgb(160, 60, 48, 8), 2);
            g.DrawLine(ink, 10, 11, 20, 11);
            g.DrawLine(ink, 10, 16, 20, 16);
            g.DrawLine(ink, 10, 21, 16, 21);
        }
        var handle = bmp.GetHicon();
        try
        {
            // FromHandle borrows the native HICON. Clone it before releasing the
            // original so NotifyIcon owns an independent managed icon.
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            Win32.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        if (_menu is not null) _menu.IsOpen = false;
        _menuOwner.Close();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
