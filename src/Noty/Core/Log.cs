using System.Diagnostics;
using System.IO;

namespace Noty.Core;

/// Set NOTY_DEBUG_DECK=1 to trace deck state transitions, as the original does.
public static class Log
{
    public static readonly bool DeckTracing =
        Environment.GetEnvironmentVariable("NOTY_DEBUG_DECK") == "1";

    private static readonly object Gate = new();

    public static void Line(string message)
    {
        var stamp = $"{DateTime.Now:HH:mm:ss.fff} noty: {message}";
        Debug.WriteLine(stamp);
        try
        {
            lock (Gate) File.AppendAllText(Paths.Log, stamp + Environment.NewLine);
        }
        catch { /* logging must never take the app down */ }
    }

    public static void Deck(string message)
    {
        if (DeckTracing) Line("deck: " + message);
    }
}
