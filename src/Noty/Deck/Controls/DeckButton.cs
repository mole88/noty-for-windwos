using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Noty.Deck.Controls;

/// Everything on the deck is custom-drawn, so the press/hover feel is here rather
/// than in a control template.
public abstract class DeckButton : Grid
{
    public event EventHandler? Click;

    /// Press and hold, then drag up or down the deck. A long press rather than a
    /// movement threshold, so a tab that drifts a couple of pixels under the click
    /// still just opens.
    /// Raised as the pointer arrives on and leaves the tab, so the deck can offer a
    /// preview of what is on it.
    public event EventHandler<bool>? HoverChanged;

    public event EventHandler? DragStarted;
    public event EventHandler<double>? DragDelta;
    public event EventHandler<double>? DragCompleted;

    /// Set by the deck while this tab is the one being dragged, so the tab can lift
    /// itself clear of its neighbours.
    public bool Dragging { get; private set; }

    private static readonly TimeSpan HoldToDrag = TimeSpan.FromMilliseconds(280);

    /// How far the pointer must travel with the button down before the tab starts
    /// following it. Small enough to feel immediate, large enough that a click with
    /// a shaky hand is still a click.
    private const double DragThreshold = 3;

    /// A drag that ended this close to where it began was a click after all.
    public const double ClickSlop = 6;

    private bool _pressed;
    private Point _origin;
    private DispatcherTimer? _hold;

    protected DeckButton()
    {
        Background = Brushes.Transparent;   // blank areas still take the click
        Cursor = Cursors.Hand;
        MouseEnter += (_, _) => { OnHover(true); HoverChanged?.Invoke(this, true); };
        MouseLeave += (_, _) =>
        {
            OnHover(false);
            HoverChanged?.Invoke(this, false);
            if (!Dragging) SetPressed(false);
        };
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            SetPressed(true);
            // Measured against the window, not against this tab: the drag moves the
            // tab under the pointer, so a position read in the tab's own space would
            // shrink back towards zero as fast as the drag pushed it out.
            _origin = e.GetPosition(null);
            CaptureMouse();
            StartHold();
            e.Handled = false;
        };
        PreviewMouseMove += (_, e) =>
        {
            // The plus and the "+N" tab are buttons, not tabs: nothing subscribes to
            // their drag, and starting one would swallow the click that follows.
            if (DragStarted is null) return;
            if (!_pressed && !Dragging) return;
            var dy = e.GetPosition(null).Y - _origin.Y;

            // Moving with the button down starts the drag at once. Waiting out a hold
            // first made the deck feel heavy: the tab did not follow the pointer
            // until the timer had run, so the first part of every drag was dead.
            if (!Dragging)
            {
                if (Math.Abs(dy) < DragThreshold) return;
                BeginDrag();
            }
            DragDelta?.Invoke(this, dy);
        };
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            CancelHold();
            var wasDragging = Dragging;
            var wasPressed = _pressed;
            var dy = e.GetPosition(null).Y - _origin.Y;

            // Cleared *before* the capture is released: releasing raises
            // LostMouseCapture synchronously, and that handler would otherwise
            // abandon the drag — dropping the tab back where it came from — a
            // moment before the drop could be acted on.
            Dragging = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            SetPressed(false);

            if (wasDragging)
            {
                e.Handled = true;
                DragCompleted?.Invoke(this, dy);
                return;
            }
            if (wasPressed && IsMouseOver)
            {
                e.Handled = true;
                Click?.Invoke(this, EventArgs.Empty);
            }
        };
        // Capture taken away mid-drag (a popup, a redraw) abandons the drag.
        LostMouseCapture += (_, _) =>
        {
            CancelHold();
            if (!Dragging) return;
            Dragging = false;
            DragCompleted?.Invoke(this, 0);
        };
    }

    private void StartHold()
    {
        if (DragStarted is null) return;    // nothing wants a drag from this one
        CancelHold();
        // Holding still also picks the tab up, so it can be lifted before it is moved.
        _hold = new DispatcherTimer { Interval = HoldToDrag };
        _hold.Tick += (_, _) =>
        {
            CancelHold();
            if (!_pressed || Dragging) return;
            BeginDrag();
        };
        _hold.Start();
    }

    private void BeginDrag()
    {
        CancelHold();
        Dragging = true;
        DragStarted?.Invoke(this, EventArgs.Empty);
    }

    private void CancelHold()
    {
        _hold?.Stop();
        _hold = null;
    }

    protected virtual void OnHover(bool hovering) { }

    private TranslateTransform? _dragShift;

    /// How far the tab has been carried from its place in the deck. Applied last, on
    /// top of the lean and the reveal slide.
    public void SetDragOffset(double dy)
    {
        if (_dragShift is null)
        {
            _dragShift = new TranslateTransform();
            RenderTransform = RenderTransform is { } current && current != Transform.Identity
                ? new TransformGroup { Children = { current, _dragShift } }
                : _dragShift;
        }
        _dragShift.Y = dy;
    }

    /// A press dips the whole tab a hair, the way the original's button style does.
    private void SetPressed(bool value)
    {
        if (_pressed == value) return;
        _pressed = value;
        var scale = EnsureScale();
        Animate(scale, ScaleTransform.ScaleXProperty, value ? 0.97 : 1, 120);
        Animate(scale, ScaleTransform.ScaleYProperty, value ? 0.97 : 1, 120);
    }

    protected ScaleTransform EnsureScale()
    {
        if (RenderTransform is TransformGroup g)
        {
            var found = g.Children.OfType<ScaleTransform>().FirstOrDefault();
            if (found is not null) return found;
            var added = new ScaleTransform(1, 1);
            g.Children.Insert(0, added);
            return added;
        }
        var scale = new ScaleTransform(1, 1);
        var group = new TransformGroup();
        group.Children.Add(scale);
        if (RenderTransform is not null && RenderTransform != Transform.Identity)
            group.Children.Add(RenderTransform);
        RenderTransform = group;
        return scale;
    }

    protected static void Animate(Animatable target, DependencyProperty prop, double to, double ms)
    {
        if (target is not IAnimatable a) return;
        a.BeginAnimation(prop, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }
}
