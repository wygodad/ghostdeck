namespace GhostDeck;

/// <summary>
/// (#3) Decides when the low-FPS toast fires; the tray feeds it every FPS-monitor tick
/// (<see cref="FpsMonitor.Ticked"/>, once a second while the monitor runs) and shows the toast.
/// Rules: only for a promoted game session in the foreground; only after that game has reached
/// the threshold at least once (a loading screen or a menu capped at 30 FPS right after the start
/// stays quiet); then the FPS has to stay below the threshold for three ticks in a row; at most
/// one toast per game per minute. A game is one process (PID): a restarted game starts fresh.
/// Holds no settings of its own - the threshold comes in with every call, 0 = off.
/// </summary>
public sealed class LowFpsAlert
{
    public const int TicksBelow = 3;
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    private sealed class Game { public int Peak; public DateTime Last = DateTime.MinValue; }
    private readonly Dictionary<int, Game> _games = new();
    private int _belowPid, _below;
    private DateTime _lastFeed = DateTime.MinValue;

    /// <summary>One tick in; returns the FPS to report when the toast should show, else null.</summary>
    public int? Feed(FpsTick t, int threshold, DateTime now)
    {
        // "in a row" means consecutive ticks: a gap (monitor switched off and on) starts over
        if (now - _lastFeed > TimeSpan.FromSeconds(3)) _below = 0;
        _lastFeed = now;
        if (threshold <= 0 || t.Pid == 0 || !t.Game || !t.Foreground) { _below = 0; return null; }
        if (!_games.TryGetValue(t.Pid, out var g))
        {
            if (_games.Count >= 16) _games.Clear();   // long-gone PIDs; nothing worth keeping
            _games[t.Pid] = g = new Game();
        }
        if (_belowPid != t.Pid) { _belowPid = t.Pid; _below = 0; }

        g.Peak = Math.Max(g.Peak, t.Fps);
        if (t.Fps >= threshold || g.Peak < threshold) { _below = 0; return null; }
        if (++_below < TicksBelow) return null;
        if (now - g.Last < Cooldown) return null;
        g.Last = now;
        _below = 0;
        return t.Fps;
    }
}
