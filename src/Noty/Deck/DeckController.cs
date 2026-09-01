using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Noty.Core;
using Noty.Deck.Controls;
using Noty.Editor;
using Noty.Interop;
using Noty.Services;

namespace Noty.Deck;

/// One deck per physical display, and the state machine that drives it.
public sealed class DeckController : IDisposable
{
    public string Device { get; }
    public DeckState State { get; private set; } = DeckState.Rest;
    public DeckManager? Manager { get; set; }

    private readonly DeckWindow _window = new();
    private readonly DispatcherTimer _idle;
    private DateTime _lastActivity = DateTime.Now;
    private Win32.POINT _lastPointer;
    private ScreenInfo _screen;

    private bool _showAll;                  // "+N more" opened into the full list
    private double _panelHeight;            // DIPs
    private NoteEditorControl? _editor;
    private string? _editorNoteId;
    private int _editorColor = -1;
    private bool _editorOnRight;

    // Only a note opened directly from the New Note action is disposable. An old
    // note that happens to contain no visible text must not vanish merely because
    // the user looked at it and closed it again.
    private string? _pendingEmptyDraftId;
    private bool _editorIsEmptyDraft;

    /// True while one of the deck's own menus is on screen.
    private bool _menuOpen;

    /// Set for the one redraw that follows the deck fanning out of the pill.
    private bool _stageReveal;

    // Store mutations notify synchronously. Flushing an editor from inside a render
    // can therefore request another render before the current one has finished.
    // Serialise those passes so the nested one cannot replace _editor while the
    // outer DropEditor is still operating on it.
    private bool _rendering;
    private bool _renderPending;

    /// How far the tab stack has been scrolled with the wheel, in DIPs.
    private double _scroll;
    private double _maxScroll;
    private Canvas? _stack;

    public bool OnRight => !Settings.DeckOnLeftEdge;

    public DeckController(ScreenInfo screen)
    {
        Device = screen.Device;
        _screen = screen;

        _window.Root.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowContextMenu();
        };
        _window.Root.PreviewMouseWheel += (_, e) =>
        {
            if (State.Phase == DeckPhase.Rest) return;
            // Over the open note the wheel belongs to the note's own text. The deck
            // only takes it out on the tabs.
            if (_editor is not null && _editor.IsMouseOver) return;
            e.Handled = true;
            Scroll(-e.Delta * 0.45);
        };
        _window.Deactivated += (_, _) =>
        {
            // A click in any other app dismisses the open note — unless it is pinned.
            if (State.ExpandedId is null || OpenNoteIsPinned) return;
            _window.Dispatcher.BeginInvoke(new Action(Dismiss), DispatcherPriority.Background);
        };

        _window.Show();
        Layout();
        Render();

        NoteStore.Shared.NotesChanged += OnNotesChanged;

        _idle = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        _idle.Tick += (_, _) => IdleTick();
    }

    private void OnNotesChanged(object? sender, EventArgs e)
    {
        // Pill height tracks the note count; an open deck redraws in place.
        if (State.Phase == DeckPhase.Rest) Layout();
        Render();
    }

    public void UpdateScreen(ScreenInfo screen)
    {
        _screen = screen;
        Layout();
        Render();
    }

    // MARK: Layout

    public void Layout()
    {
        var s = _screen;

        // One size for every state. Resizing the window on each transition made the
        // pill blink as the layered window was torn down and repainted, and made the
        // deck draw against the far edge for a frame as a note opened. The window is
        // click-through wherever nothing is drawn, so leaving it at full size costs
        // nothing.
        var w = (int)Math.Round(DeckGeom.ExpandedWidth * s.Scale);
        var h = s.Work.Height;
        var x = OnRight ? s.Bounds.Right - w : s.Bounds.Left;
        var y = s.Work.Top;

        _window.PlaceDevice(x, y, w, h);
        _panelHeight = h / s.Scale;
        _window.Root.Width = w / s.Scale;
        _window.Root.Height = _panelHeight;
    }

    public void RefreshLevel()
    {
        _window.Topmost = true;
        _window.Raise();
    }

    // MARK: Transitions

    private void SetState(DeckState next)
    {
        var old = State;
        if (old.Equals(next)) return;
        Log.Deck($"setState {old} -> {next}");

        State = next;
        if (next.Phase == DeckPhase.Rest) _scroll = 0;
        // The stagger belongs to the deck fanning out, and nothing else. Replaying it
        // on every redraw meant the whole deck flew back in on each autosave, which
        // is what made the fan look like it was dropping frames.
        if (next.Phase == DeckPhase.Fan && old.Phase == DeckPhase.Rest) _stageReveal = true;
        Render();

        NoteActivity();
        _window.SetAcceptsKeys(next.ExpandedId is not null);
        if (next.Phase == DeckPhase.Rest)
        {
            _idle.Stop();
            _showAll = false;
        }
        else if (!_idle.IsEnabled)
        {
            _lastActivity = DateTime.Now;
            _lastPointer = Screens.Cursor;
            _idle.Start();
        }
    }

    /// Anything the user does keeps the deck awake.
    public void NoteActivity() => _lastActivity = DateTime.Now;

    /// A deck left untouched tidies itself away: the fan after a few seconds, an open
    /// note after a minute. Polling the pointer avoids needing global mouse hooks.
    private void IdleTick()
    {
        // A menu open over the deck is the deck being used, even though the pointer
        // has walked off the strip to reach it.
        if (_menuOpen)
        {
            NoteActivity();
            return;
        }
        var now = Screens.Cursor;
        if (State.Phase == DeckPhase.Fan && !HotZone.Contains(now))
        {
            var z = HotZone;
            Log.Deck($"left the strip: cursor {now.X},{now.Y} vs {z.Left}..{z.Right} x {z.Top}..{z.Bottom}");
            Collapse();
            return;
        }
        if (Math.Abs(now.X - _lastPointer.X) > 2 || Math.Abs(now.Y - _lastPointer.Y) > 2)
        {
            _lastPointer = now;
            _lastActivity = DateTime.Now;
        }
        var idle = DateTime.Now - _lastActivity;
        switch (State.Phase)
        {
            case DeckPhase.Fan when idle > Settings.FanIdleTimeout:
                Collapse();
                break;
            case DeckPhase.Expanded when idle > Settings.NoteIdleTimeout && !OpenNoteIsPinned:
                Dismiss();
                break;
        }
    }

    /// The window is wide enough to hold an open note, but the deck itself only
    /// occupies the strip against the screen edge — that strip is what "leaving the
    /// deck" means.
    public Win32.RECT HotZone
    {
        get
        {
            var s = _screen;
            var strip = (int)Math.Round((DeckGeom.FanWidth + 20) * s.Scale);
            return State.Phase == DeckPhase.Rest
                ? EdgeStrip
                : new Win32.RECT
                {
                    Left = OnRight ? s.Bounds.Right - strip : s.Bounds.Left,
                    Right = OnRight ? s.Bounds.Right : s.Bounds.Left + strip,
                    Top = s.Work.Top,
                    Bottom = s.Work.Bottom,
                };
        }
    }

    /// The dormant detection strip — the pill's own footprint.
    public Win32.RECT EdgeStrip
    {
        get
        {
            var s = _screen;
            var w = (int)Math.Round(Math.Max(DeckGeom.PillWidth + 2, Settings.EdgeWidth) * s.Scale);
            var h = (int)Math.Round(DeckGeom.PillHeight(
                Math.Max(1, NoteStore.Shared.Active.Count), s.Scale) * s.Scale);
            var top = s.Work.Top + (s.Work.Height - h) / 2;
            return new Win32.RECT
            {
                Left = OnRight ? s.Bounds.Right - w : s.Bounds.Left,
                Right = OnRight ? s.Bounds.Right : s.Bounds.Left + w,
                Top = top,
                Bottom = top + h,
            };
        }
    }

    public void PointerEntered()
    {
        NoteActivity();
        if (State.Phase != DeckPhase.Rest) return;
        Log.Deck("pointerEntered");
        Manager?.DeckDidActivate(this);
        SetState(DeckState.Fan);
    }

    public void Expand(string id, bool discardIfStillEmpty = false)
    {
        _pendingEmptyDraftId = discardIfStillEmpty ? id : null;
        NoteActivity();
        Manager?.DeckDidActivate(this);
        SetState(DeckState.Expanded(id));
        _window.Focus(foreground: true);
        _editor?.FocusText();
    }

    /// Closing a note steps back to the deck — the tabs stay where they were. Only
    /// leaving the deck entirely puts it back to sleep.
    public void Collapse()
    {
        if (State.ExpandedId is not null) SetState(DeckState.Fan);
        else SetState(DeckState.Rest);
    }

    /// Dismiss the whole deck, note and tabs together.
    public void Dismiss() => SetState(DeckState.Rest);

    public void CollapseToRest() => SetState(DeckState.Rest);

    private bool OpenNoteIsPinned =>
        State.ExpandedId is { } id && (NoteStore.Shared.Get(id)?.Pinned ?? false);

    // MARK: Rendering

    private void Render()
    {
        if (_rendering)
        {
            _renderPending = true;
            return;
        }

        _rendering = true;
        try
        {
            do
            {
                _renderPending = false;
                RenderCore();
            }
            while (_renderPending);
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RenderCore()
    {
        var root = _window.Root;
        // Everything except the open note goes. Detaching the note — even to put it
        // straight back — pulls a focused text view out of the visual tree, and the
        // keystrokes that land while it is out are simply lost. The deck redraws on
        // every save, so that showed up as letters going missing while typing.
        for (var i = root.Children.Count - 1; i >= 0; i--)
            if (!ReferenceEquals(root.Children[i], _editor))
                root.Children.RemoveAt(i);
        _stack = null;
        _maxScroll = 0;

        var store = NoteStore.Shared;
        var active = store.Active;
        var w = root.Width;
        var h = Math.Max(1, _panelHeight);
        var onRight = OnRight;

        if (State.Phase == DeckPhase.Rest)
        {
            DropEditor();
            var pill = new PillControl(active, _screen.Scale);
            Canvas.SetLeft(pill, onRight ? w - DeckGeom.PillWidth - 1 : 1);
            Canvas.SetTop(pill, Math.Max(0,
                (h - DeckGeom.PillHeight(active.Count, _screen.Scale)) / 2));
            root.Children.Add(pill);
            return;
        }

        // A hit-testable strip along the edge, behind the tabs. Without it the whole
        // fan except the tabs themselves is click-through, and the pill's menu has
        // nothing left to right-click: reaching for the pill opens the fan first.
        // Only as wide as the deck, so it covers almost none of what is behind it.
        // Not Brushes.Transparent: a layered window decides what is click-through
        // from the alpha it actually painted, so a fully transparent fill is passed
        // straight through to the app underneath. One step of alpha is invisible and
        // enough to catch the click.
        var strip = new Border
        {
            Width = DeckGeom.FanWidth,
            Height = h,
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
        };
        Canvas.SetLeft(strip, onRight ? w - DeckGeom.FanWidth : 0);
        Canvas.SetTop(strip, 0);
        root.Children.Add(strip);

        // Everything on the deck rides one canvas, so the wheel can carry the whole
        // stack up and down by moving a single element rather than laying out again.
        var stack = new Canvas { Width = w, Height = h, Background = null };
        _stack = stack;
        root.Children.Add(stack);

        var visible = _showAll ? active : active.Take(Settings.FanLimit).ToList();
        var hidden = Math.Max(0, active.Count - Settings.FanLimit);
        var showsMore = !_showAll && hidden > 0;
        var longest = visible.Count == 0
            ? 0
            : visible.Max(n => Ink.MeasureTabLabel(n.DisplayTitle));
        var lay = DeckGeom.Layout(h, Math.Max(1, visible.Count), showsMore,
                                  Settings.DeckStyle, longest, allowOverflow: _showAll);

        var y = lay.Top;
        var stage = 0;

        // The dashed rule the deck hangs from, right at the screen edge.
        var rule = Shapes.EdgeRule(Math.Min(lay.StackHeight + 26, lay.Cap),
                                   NoteColor.Tint(Colors.White, 0.35));
        Canvas.SetLeft(rule, onRight ? w - 3.5 : 3.5);
        Canvas.SetTop(rule, Math.Max(0, lay.Top - 13));
        stack.Children.Add(rule);

        if (visible.Count == 0)
        {
            var empty = new EmptyTab(lay.ItemHeight, lay.Pitch, onRight);
            empty.Click += (_, _) => Actions.NewNote();
            Place(empty, y, DeckGeom.TabWidth, stage++);
            y += lay.Pitch;
        }

        for (var i = 0; i < visible.Count; i++)
        {
            var note = visible[i];
            var isOpen = State.ExpandedId == note.Id;
            DeckButton tab;
            double footprint;
            if (Settings.DeckStyle == DeckStyle.Compact)
            {
                tab = new ChipTab(note, isOpen, onRight);
                footprint = DeckGeom.ChipWidth;
            }
            else
            {
                tab = new VerticalTab(note, isOpen, lay.ItemHeight, lay.Pitch, onRight);
                footprint = DeckGeom.TabWidth;
            }
            tab.Click += (_, _) => Toggle(note.Id);
            AttachDrag(tab, note, lay);
            AttachNoteMenu(tab, note);
            // Placed by the width the deck reserves, not the width the tab draws:
            // the extra bleed runs off the screen edge, so the lean cannot open a
            // wedge of background between the tab and the edge it is stuck to.
            Place(tab, y, footprint, stage++);
            y += lay.Pitch;
        }

        // Undo the lap before the "+N" tab and the plus button — the original adds
        // the gap back on top of the negative stack spacing for exactly this reason.
        y += lay.ItemHeight - lay.Pitch;

        if (showsMore)
        {
            var more = new MoreTab(hidden, lay.MoreHeight, onRight);
            more.Click += (_, _) =>
            {
                _showAll = true;
                NoteActivity();
                Render();
            };
            y += lay.MoreGap;
            Place(more, y, DeckGeom.TabWidth, stage++);
            y += lay.MoreHeight;
        }

        var plus = new PlusButton();
        plus.Click += (_, _) => Actions.NewNote();
        y += DeckGeom.PlusGap;
        Place(plus, y, DeckGeom.PlusSize, stage, centred: true);

        // A deck taller than the screen scrolls under the wheel rather than being
        // squeezed further. The bottom of the last thing placed is the whole extent.
        _stageReveal = false;

        _maxScroll = Math.Max(0, y + DeckGeom.PlusSize + 12 - h);
        _scroll = Math.Clamp(_scroll, 0, _maxScroll);
        Canvas.SetTop(stack, -_scroll);

        // Declared last so it covers the deck, flush to the screen edge.
        if (State.ExpandedId is { } openId && store.Get(openId) is { } openNote)
        {
            // A redraw must not throw the open note away and build it again: the deck
            // redraws on every autosave, and a fresh editor would take the caret, the
            // undo history and the find bar with it. Only a different note — or a
            // different colour or screen edge, both of which repaint its physical
            // shape — earns a new one.
            var reuse = _editor is not null && _editorNoteId == openId &&
                        _editorColor == openNote.Color && _editorOnRight == onRight;
            NoteEditorControl editor;
            if (reuse)
            {
                editor = _editor!;
                editor.RefreshChrome(openNote);
            }
            else
            {
                // Repainting the same note for a colour/edge change is not closing
                // it. Carry its draft status into the replacement editor; only a
                // move to another note abandons the old draft.
                var emptyDraft = _editorNoteId == openId
                    ? _editorIsEmptyDraft
                    : _pendingEmptyDraftId == openId;
                DropEditor(closing: _editorNoteId != openId);
                editor = new NoteEditorControl(openNote, this, onRight)
                {
                    Width = DeckGeom.EditorWidth,
                    Height = DeckGeom.EditorHeight,
                };
                _editor = editor;
                _editorNoteId = openId;
                _editorColor = openNote.Color;
                _editorOnRight = onRight;
                _editorIsEmptyDraft = emptyDraft;
                _pendingEmptyDraftId = null;
            }

            var idx = visible.FindIndex(n => n.Id == openId);
            var top = EditorTop(lay, idx < 0 ? 0 : idx);
            Canvas.SetLeft(editor, onRight ? w - DeckGeom.EditorWidth : 0);
            Canvas.SetTop(editor, top);
            // A reused editor was never detached, so it keeps its place in the child
            // list. An explicit z-index — rather than being added last — is what
            // keeps it over the deck either way.
            if (!root.Children.Contains(editor)) root.Children.Add(editor);
            Panel.SetZIndex(editor, 10);
            if (!reuse)
            {
                PullIn(editor, onRight);
                editor.FocusText();
            }
        }
        else DropEditor();

        void Place(FrameworkElement el, double top, double width, int index, bool centred = false)
        {
            var left = onRight ? w - width : 0;
            if (centred) left = onRight ? w - width - 10 : 10;
            Canvas.SetLeft(el, left);
            Canvas.SetTop(el, top);
            stack.Children.Add(el);
            if (_stageReveal) Stage(el, index, onRight);
        }
    }

    /// The wheel carries the deck past the edge of the screen. The first turn also
    /// opens the "+N" pile — there is no point scrolling a deck that is holding most
    /// of itself back.
    private void Scroll(double by)
    {
        NoteActivity();
        if (!_showAll && NoteStore.Shared.Active.Count > Settings.FanLimit)
        {
            _showAll = true;
            Render();
        }
        if (_maxScroll <= 0 || _stack is null) return;

        var next = Math.Clamp(_scroll + by, 0, _maxScroll);
        if (Math.Abs(next - _scroll) < 0.01) return;
        _scroll = next;
        Canvas.SetTop(_stack, -_scroll);
    }

    /// Press and hold a tab, then drag it up or down to reshuffle the deck.
    private void AttachDrag(DeckButton tab, Note note, DeckLayout lay)
    {
        tab.DragStarted += (_, _) =>
        {
            NoteActivity();
            // Only the tab being dragged is raised. Lifting every tab would reorder
            // neighbours and break the shingle; the rest keep their paint order.
            Panel.SetZIndex(tab, 900);
            (tab as VerticalTab)?.SetLifted(true);
        };
        tab.DragDelta += (_, dy) =>
        {
            NoteActivity();
            tab.SetDragOffset(dy);
        };
        tab.DragCompleted += (_, dy) =>
        {
            NoteActivity();
            Panel.SetZIndex(tab, 0);
            (tab as VerticalTab)?.SetLifted(false);
            tab.SetDragOffset(0);
            var slots = (int)Math.Round(dy / Math.Max(1, lay.Pitch));
            if (slots != 0) NoteStore.Shared.Reorder(note.Id, slots);
            else Render();
        };
    }

    /// The open note is written out before it goes, so nothing typed since the last
    /// save is lost to the close.
    ///
    /// It also has to come off the canvas here. A redraw deliberately leaves the
    /// open note attached, so dropping only the reference left the old note on
    /// screen — hanging around after Close until something else forced a redraw, and
    /// sitting over the next note when one was opened from under it.
    private void DropEditor(bool closing = true)
    {
        string? discard = null;
        if (_editor is not null)
        {
            _editor.Flush();
            _window.Root.Children.Remove(_editor);
            if (closing && _editorIsEmptyDraft &&
                string.IsNullOrWhiteSpace(NoteStore.Shared.Get(_editorNoteId!)?.Body))
            {
                discard = _editorNoteId;
            }
        }
        _editor = null;
        _editorNoteId = null;
        _editorColor = -1;
        _editorOnRight = OnRight;
        _editorIsEmptyDraft = false;

        // Render() serialises synchronous store notifications, so discarding now is
        // safe and leaves no interval in which input could reopen the draft before a
        // queued deletion removed it from underneath the new editor.
        if (discard is not null) NoteStore.Shared.DiscardIfEmpty(discard);
    }

    private void Toggle(string id)
    {
        if (State.ExpandedId == id) Collapse();
        else Expand(id);
    }

    /// Keep the open note level with its own tab, without letting it run off-screen.
    private static double EditorTop(DeckLayout lay, int index)
    {
        var ideal = lay.Center(index) - DeckGeom.EditorHeight / 2;
        var lowest = Math.Max(10, lay.PanelHeight - DeckGeom.EditorHeight - 10);
        return Math.Min(Math.Max(10, ideal), lowest);
    }

    /// The 45 ms shingle: each tab slides in from beyond the edge, one after the next.
    private static void Stage(FrameworkElement el, int index, bool onRight)
    {
        var from = onRight ? DeckGeom.TabWidth + 24 : -(DeckGeom.TabWidth + 24);
        var slide = new TranslateTransform(from, 0);
        el.RenderTransform = el.RenderTransform is { } t && t != Transform.Identity
            ? new TransformGroup { Children = { t, slide } }
            : slide;
        el.Opacity = 0;

        var delay = TimeSpan.FromMilliseconds(index * 45);
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 };

        slide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(360))
            {
                BeginTime = delay,
                EasingFunction = ease,
            });
        el.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = delay });
    }

    /// The note emerging from its tab: a short slide off the edge, a touch of scale
    /// anchored there, and a fade. A full-width slide reads as a window flying in.
    private static void PullIn(FrameworkElement el, bool onRight)
    {
        var slide = new TranslateTransform(onRight ? 40 : -40, 0);
        var scale = new ScaleTransform(0.965, 0.965);
        el.RenderTransformOrigin = new Point(onRight ? 1 : 0, 0.5);
        el.RenderTransform = new TransformGroup { Children = { scale, slide } };
        el.Opacity = 0;

        var dur = TimeSpan.FromMilliseconds(340);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        slide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, dur) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, dur) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, dur) { EasingFunction = ease });
        el.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    private void AttachNoteMenu(FrameworkElement el, Note note)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItemFor(note.Pinned ? "Unpin" : "Pin",
            () => NoteStore.Shared.TogglePin(note.Id)));
        menu.Items.Add(MenuItemFor("Archive", () => NoteStore.Shared.SetArchived(note.Id, true)));
        menu.Items.Add(MenuItemFor($"Cycle colour  {Settings.ScColour}",
            () => NoteStore.Shared.CycleColor(note.Id)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("Delete", () =>
        {
            NoteStore.Shared.Delete(note.Id);
            if (State.ExpandedId == note.Id) Collapse();
        }));
        el.ContextMenu = Menu(menu);
        el.ContextMenuOpening += (_, _) => PrepareForMenu();
    }

    private static MenuItem MenuItemFor(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void ShowContextMenu()
    {
        var menu = Menu(Actions.BuildMainMenu(this));
        menu.PlacementTarget = _window.Root;
        PrepareForMenu();
        menu.IsOpen = true;
    }

    /// A menu has to be able to close by clicking past it, and it must not be shut
    /// out from under the pointer while it is open.
    ///
    /// Both come from the deck being a window that refuses activation: a popup on an
    /// unfocused window never hears the click that lands somewhere else, and the
    /// pointer moving onto the menu counts as leaving the strip the deck lives in,
    /// which is what made picking a colour take the rest of the deck with it.
    private ContextMenu Menu(ContextMenu menu)
    {
        menu.Opened += (_, _) =>
        {
            _menuOpen = true;
            NoteActivity();
        };
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            NoteActivity();
            _window.SetAcceptsKeys(State.ExpandedId is not null);
        };
        return menu;
    }

    private void PrepareForMenu()
    {
        _window.SetAcceptsKeys(true);
        _window.Focus(foreground: true);
    }

    // MARK: Key handling for the expanded note

    /// Called by the editor before the text view sees the key.
    public bool HandleNoteKey(KeyEventArgs e)
    {
        if (State.ExpandedId is not { } id) return false;
        NoteActivity();

        // Close first: while the find bar is up it takes the key instead.
        if (Settings.ScClose.Matches(e))
        {
            if (_editor?.FindVisible == true) _editor.HideFind();
            else Collapse();
            return true;
        }
        if (Settings.ScArchiveNote.Matches(e))
        {
            NoteStore.Shared.SetArchived(id, true);
            Collapse();
            return true;
        }
        if (Settings.ScDelete.Matches(e))
        {
            NoteStore.Shared.Delete(id);
            Collapse();
            return true;
        }
        if (Settings.ScFind.Matches(e))
        {
            _editor?.ToggleFind();
            return true;
        }
        if (Settings.ScTask.Matches(e))
        {
            _editor?.ToggleTaskLine();
            return true;
        }
        if (Settings.ScPin.Matches(e))
        {
            NoteStore.Shared.TogglePin(id);
            return true;
        }
        if (Settings.ScColour.Matches(e))
        {
            NoteStore.Shared.CycleColor(id);
            return true;
        }
        if (Settings.ScBigger.Matches(e))
        {
            Actions.StepFontSize(1.5);
            return true;
        }
        if (Settings.ScSmaller.Matches(e))
        {
            Actions.StepFontSize(-1.5);
            return true;
        }
        return false;
    }

    public void Redraw() => Render();

    public void Dispose()
    {
        NoteStore.Shared.NotesChanged -= OnNotesChanged;
        _idle.Stop();
        // Application shutdown and monitor removal do not transition the state
        // through Render, so explicitly finish the same close path here.
        DropEditor();
        _window.Close();
    }
}
