using System.Windows.Threading;
using Noty.Core;
using Noty.Interop;

namespace Noty.Deck;

/// Keeps one deck alive per display and rebuilds the set when displays change.
///
/// The pointer is polled rather than tracked: a dormant window carries
/// WS_EX_NOACTIVATE and sits behind whatever you are working in, so mouse messages
/// are not a reliable way to notice the pointer arriving at the edge — and polling
/// is what the original falls back to for leaving anyway.
public sealed class DeckManager : IDisposable
{
    private readonly Dictionary<string, DeckController> _decks = new();
    private readonly DispatcherTimer _poll;
    private readonly Dictionary<string, bool> _outside = new();
    private string _layoutSignature = "";
    private DateTime _lastDisplayCheck = DateTime.Now;
    private static readonly TimeSpan DisplayCheckEvery = TimeSpan.FromSeconds(2);

    public IReadOnlyDictionary<string, DeckController> Decks => _decks;

    public DeckManager()
    {
        var screens = Screens.All();
        _layoutSignature = Signature(screens);
        Rebuild(screens);
        _poll = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(90),
        };
        _poll.Tick += (_, _) => Tick();
        _poll.Start();
    }

    /// Displays are tracked by device name, bounds and scale together — hot-plugging
    /// or a resolution change rebuilds the decks.
    private static string Signature(List<ScreenInfo> screens) =>
        string.Join("|", screens.Select(s =>
            $"{s.Device}:{s.Bounds.Left},{s.Bounds.Top},{s.Bounds.Right},{s.Bounds.Bottom}@{s.Scale}"));

    private void Tick()
    {
        // Enumerating monitors means a P/Invoke and a marshalled struct per display.
        // Doing that every tick stole enough time on the UI thread to make the fan
        // animation stutter, and displays do not come and go that often.
        if (DateTime.Now - _lastDisplayCheck > DisplayCheckEvery)
        {
            _lastDisplayCheck = DateTime.Now;
            var screens = Screens.All();
            var signature = Signature(screens);
            if (signature != _layoutSignature)
            {
                _layoutSignature = signature;
                Rebuild(screens);
                return;
            }
        }

        // The deck wakes on the pointer *arriving*, not on it merely being there. A
        // fan left untouched tidies itself away after a few seconds, and without this
        // the very next poll would find the pointer still parked on the pill and open
        // it again — the deck would flap open and shut for as long as you left the
        // mouse where it was. Tracking areas give the original this for free.
        var p = Screens.Cursor;
        foreach (var deck in _decks.Values)
        {
            var inside = deck.EdgeStrip.Contains(p);
            if (inside && deck.State.Phase == DeckPhase.Rest &&
                _outside.GetValueOrDefault(deck.Device, true))
            {
                deck.PointerEntered();
            }
            _outside[deck.Device] = !inside;
        }
    }

    public void Rebuild(List<ScreenInfo>? screens = null)
    {
        screens ??= Screens.All();
        var live = screens.ToDictionary(s => s.Device);

        foreach (var device in _decks.Keys.Where(d => !live.ContainsKey(d)).ToList())
        {
            _decks[device].Dispose();
            _decks.Remove(device);
        }
        foreach (var (device, screen) in live)
        {
            if (_decks.TryGetValue(device, out var existing)) existing.UpdateScreen(screen);
            else _decks[device] = new DeckController(screen) { Manager = this };
        }
        Log.Deck($"rebuild — {_decks.Count} display(s)");
    }

    /// Only one deck is open at a time — the one the pointer entered.
    public void DeckDidActivate(DeckController active)
    {
        foreach (var d in _decks.Values)
            if (!ReferenceEquals(d, active)) d.CollapseToRest();
    }

    public void RefreshAll()
    {
        foreach (var d in _decks.Values)
        {
            d.RefreshLevel();
            d.Layout();
            d.Redraw();
        }
    }

    /// Deck on the screen holding the pointer, else the first one there is.
    public DeckController? Focused
    {
        get
        {
            var s = Screens.At(Screens.Cursor);
            if (s is not null && _decks.TryGetValue(s.Device, out var d)) return d;
            return _decks.Values.FirstOrDefault();
        }
    }

    public void Dispose()
    {
        _poll.Stop();
        foreach (var d in _decks.Values) d.Dispose();
        _decks.Clear();
    }
}
