using System.IO;
using System.Text.Json;

namespace GhostDeck;

/// <summary>Where a profile / EC change came from (for the history log).</summary>
public enum ChangeSource
{
    Startup, Hotkey, Tray, Panel, AutoAc, FanCurve, ExternalSync,
    ChargeLimit, CoolerBoost, Firmware, Test, Thermal,
}

public sealed record LogEntry(DateTime Time, ChangeSource Source, string Detail, string Result);

/// <summary>
/// In-memory ring buffer of the last EC changes (profile switches, charge-limit, fan curve,
/// cooler boost, external syncs, firmware events), persisted to a small JSON file so the history
/// survives a restart and can be attached to a model-support report. Newest entry first.
///
/// The recorded "readback" is informational only — several EC bytes are dynamic (see TECHNICAL
/// §19.4), so a mismatch here is NOT treated as an error; the log just shows what was read back.
/// </summary>
public static class ChangeLog
{
    private const int Max = 300;
    private static readonly LinkedList<LogEntry> _items = new();
    private static readonly object _lock = new();

    public static event Action? Changed;

    private static string FilePath => Path.Combine(AppSettings.Dir, "changelog.json");

    public static void Add(ChangeSource src, string detail, string result = "")
    {
        var e = new LogEntry(DateTime.Now, src, detail, result);
        lock (_lock)
        {
            _items.AddFirst(e);
            while (_items.Count > Max) _items.RemoveLast();
            SaveNoLock();
        }
        Changed?.Invoke();
    }

    public static IReadOnlyList<LogEntry> Recent(int n)
    {
        lock (_lock) return _items.Take(n).ToList();
    }

    public static IReadOnlyList<LogEntry> All()
    {
        lock (_lock) return _items.ToList();
    }

    public static void Clear()
    {
        lock (_lock) { _items.Clear(); SaveNoLock(); }
        Changed?.Invoke();
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var arr = JsonSerializer.Deserialize<List<LogEntry>>(File.ReadAllText(FilePath));
            if (arr == null) return;
            lock (_lock)
            {
                _items.Clear();
                foreach (var e in arr.Take(Max)) _items.AddLast(e);
            }
        }
        catch { }
    }

    private static void SaveNoLock()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items.ToList()));
        }
        catch { }
    }

    public static string SourceLabel(ChangeSource s) => s switch
    {
        ChangeSource.Startup      => Lang.T("log_src_startup"),
        ChangeSource.Hotkey       => Lang.T("log_src_hotkey"),
        ChangeSource.Tray         => Lang.T("log_src_tray"),
        ChangeSource.Panel        => Lang.T("log_src_panel"),
        ChangeSource.AutoAc       => Lang.T("log_src_autoac"),
        ChangeSource.FanCurve     => Lang.T("log_src_fancurve"),
        ChangeSource.ExternalSync => Lang.T("log_src_external"),
        ChangeSource.ChargeLimit  => Lang.T("log_src_charge"),
        ChangeSource.CoolerBoost  => Lang.T("log_src_cooler"),
        ChangeSource.Firmware     => Lang.T("log_src_firmware"),
        ChangeSource.Test         => Lang.T("log_src_test"),
        ChangeSource.Thermal      => Lang.T("log_src_thermal"),
        _                         => s.ToString(),
    };

    /// <summary>Plain-text dump (newest first) for the clipboard / model report.</summary>
    public static string ToText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var e in All())
            sb.Append(e.Time.ToString("yyyy-MM-dd HH:mm:ss")).Append("  [")
              .Append(SourceLabel(e.Source)).Append("]  ").Append(e.Detail)
              .Append(string.IsNullOrEmpty(e.Result) ? "" : "  →  " + e.Result)
              .AppendLine();
        return sb.ToString();
    }
}
