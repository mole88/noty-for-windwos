using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Noty.Core;
using Noty.Interop;

namespace Noty.Windows;

/// "Ctrl+Backspace deletes it with ten seconds to undo." — a floating confirmation
/// that follows NoteStore.PendingUndo.
public sealed class UndoToast
{
    public static UndoToast Shared { get; } = new();

    private Window? _window;
    private DispatcherTimer? _tick;
    private TextBlock? _countdown;

    public void Start() => NoteStore.Shared.PendingUndoChanged += (_, _) =>
    {
        if (NoteStore.Shared.PendingUndo is { } p) Show(p);
        else Hide();
    };

    private void Show(NoteStore.PendingDelete pending)
    {
        Hide();

        var label = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(pending.Note.Title)
                ? "Note deleted"
                : $"Deleted “{pending.Note.DisplayTitle}”",
            FontFamily = Ink.SystemFace,
            FontSize = 12,
            Foreground = NoteColor.Tint(Colors.White, 0.92),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        _countdown = new TextBlock
        {
            FontFamily = Ink.SystemFace,
            FontSize = 11,
            Foreground = NoteColor.Tint(Colors.White, 0.45),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
        };

        var undo = new Button
        {
            Content = "Undo",
            FontFamily = Ink.SystemFace,
            FontSize = 11.5,
            Padding = new Thickness(10, 3, 10, 3),
            Cursor = Cursors.Hand,
            Foreground = Brushes.White,
            Background = NoteColor.Tint(Colors.White, 0.16),
            BorderThickness = new Thickness(0),
        };
        undo.Click += (_, _) => NoteStore.Shared.UndoDelete();

        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(14, 0, 10, 0) };
        DockPanel.SetDock(undo, Dock.Right);
        DockPanel.SetDock(_countdown, Dock.Right);
        row.Children.Add(undo);
        row.Children.Add(_countdown);
        row.Children.Add(label);

        var w = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Manual,
            Width = 300,
            Height = 46,
            Content = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x22, 0x22, 0x26)),
                Child = row,
            },
        };

        // On the screen the pointer is on, just above the taskbar.
        var screen = Screens.At(Screens.Cursor) ?? Screens.All().FirstOrDefault();
        if (screen is not null)
        {
            var work = screen.WorkDips;
            w.Left = work.Left + (work.Width - w.Width) / 2;
            w.Top = work.Bottom - w.Height - 34;
        }
        w.Show();
        _window = w;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tick.Tick += (_, _) =>
        {
            var left = (pending.Deadline - DateTime.Now).TotalSeconds;
            if (_countdown is not null) _countdown.Text = $"{Math.Max(0, Math.Ceiling(left))} s";
            if (left <= 0) Hide();
        };
        _tick.Start();
    }

    private void Hide()
    {
        _tick?.Stop();
        _tick = null;
        _window?.Close();
        _window = null;
        _countdown = null;
    }
}
