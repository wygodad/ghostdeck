namespace GhostDeck;

/// <summary>One point of the local hardware history (temps in °C, fan duty in %, load in %).</summary>
public readonly record struct HwSample(
    DateTime Time, short CpuTemp, short GpuTemp, short CpuFan, short GpuFan,
    int CpuRpm, int GpuRpm, short CpuLoad, ProfileId Profile = ProfileId.Balanced);

/// <summary>
/// In-memory ring buffer of hardware samples fed by the tray poll (one sample every 3 s,
/// ~60 min capacity). Local only, by design: nothing is written to disk and nothing leaves
/// the machine; the buffer starts empty on every launch. The Status "History" sub-tab reads it.
/// </summary>
public static class HwHistory
{
    private const int Cap = 1200;   // 60 min at the 3 s poll interval
    private static readonly HwSample[] _buf = new HwSample[Cap];
    private static int _count, _head;
    private static readonly object _lock = new();

    /// <summary>True once any sample carried a fan RPM (models without tach addresses never do).</summary>
    public static bool HasRpm { get; private set; }

    public static void Add(HwSample s)
    {
        lock (_lock)
        {
            _buf[_head] = s;
            _head = (_head + 1) % Cap;
            if (_count < Cap) _count++;
            if (s.CpuRpm > 0 || s.GpuRpm > 0) HasRpm = true;
        }
    }

    /// <summary>Samples from the last <paramref name="span"/>, oldest first.</summary>
    public static List<HwSample> Window(TimeSpan span)
    {
        var cut = DateTime.Now - span;
        var list = new List<HwSample>();
        lock (_lock)
        {
            int start = (_head - _count + Cap) % Cap;
            for (int i = 0; i < _count; i++)
            {
                var s = _buf[(start + i) % Cap];
                if (s.Time >= cut) list.Add(s);
            }
        }
        return list;
    }
}
