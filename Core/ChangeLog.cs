using System.IO;
using System.Text.Json;

namespace GhostDeck;

/// <summary>Where a profile / EC change came from (for the history log).
/// New members go at the END — the JSON file stores these as ints.</summary>
public enum ChangeSource
{
    Startup, Hotkey, Tray, Panel, AutoAc, FanCurve, ExternalSync,
    ChargeLimit, CoolerBoost, Firmware, Test, Thermal, Cli, Display, Game, Restore, Scene,
    Schedule, Battery,
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
    public const int Max = 300;
    private static readonly LinkedList<LogEntry> _items = new();
    private static readonly object _lock = new();

    public static event Action? Changed;

    private static string FilePath => Path.Combine(AppSettings.Dir, "changelog.json");

    /// <summary>
    /// The complete history, one line per entry, appended for ever: the in-app list keeps the
    /// last <see cref="Max"/> entries (changelog.json), which a busy day of testing fills in
    /// hours; this file keeps everything, one file per year, and is never cleared by the app.
    /// Tab-separated: time, source, detail, readback.
    /// </summary>
    public static string HistoryPath => Path.Combine(AppSettings.Dir, $"history-{DateTime.Now:yyyy}.log");

    /// <summary>
    /// Every entry ever written: the history files of all years, parsed back, plus the in-app list
    /// (which has the entries of a build that wrote no history file yet). Duplicates are dropped by
    /// time + source + detail. Newest first. A line that does not parse is skipped.
    /// </summary>
    public static List<LogEntry> Everything()
    {
        var all = new Dictionary<(DateTime, ChangeSource, string), LogEntry>();
        foreach (var e in All()) all[KeyOf(e)] = e;
        foreach (var e in FromFiles())
            if (!all.ContainsKey(KeyOf(e))) all[KeyOf(e)] = e;
        return all.Values.OrderByDescending(e => e.Time).ToList();
    }

    /// <summary>
    /// What makes two entries the same: the time to the second (the in-app list keeps milliseconds,
    /// the file keeps seconds), the source and the detail.
    /// </summary>
    public static (DateTime Time, ChangeSource Source, string Detail) KeyOf(LogEntry e) =>
        (new DateTime(e.Time.Year, e.Time.Month, e.Time.Day, e.Time.Hour, e.Time.Minute, e.Time.Second), e.Source, Plain(e.Detail));

    private static string Line(LogEntry e) =>
        $"{e.Time:yyyy-MM-dd HH:mm:ss}\t{e.Source}\t{Plain(e.Detail)}\t{Plain(e.Result)}";

    /// <summary>The history files of every year, parsed; a line that does not parse is skipped.</summary>
    private static List<LogEntry> FromFiles()
    {
        var list = new List<LogEntry>();
        try
        {
            if (!Directory.Exists(AppSettings.Dir)) return list;
            foreach (var f in Directory.GetFiles(AppSettings.Dir, "history-*.log"))
                foreach (var line in File.ReadLines(f))
                {
                    var p = line.Split('\t');
                    if (p.Length < 3) continue;
                    if (!DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var t)) continue;
                    if (!Enum.TryParse<ChangeSource>(p[1], out var src)) src = ChangeSource.Startup;
                    list.Add(new LogEntry(t, src, p[2], p.Length > 3 ? p[3] : ""));
                }
        }
        catch (Exception ex) { AppLifecycle.Note("history files could not be read: " + ex.Message); }
        return list;
    }

    /// <summary>
    /// Once per start: every entry the in-app list holds that the history files do not (the
    /// entries from before the file existed, up to 300) is written to the file of its year, oldest
    /// first, so the file is complete from the first start of a build that keeps one. Returns how
    /// many lines were written.
    /// </summary>
    public static int BackfillHistory()
    {
        try
        {
            var known = new HashSet<(DateTime, ChangeSource, string)>(FromFiles().Select(KeyOf));
            var missing = All().Where(e => !known.Contains(KeyOf(e))).OrderBy(e => e.Time).ToList();
            if (missing.Count == 0) return 0;
            Directory.CreateDirectory(AppSettings.Dir);
            foreach (var group in missing.GroupBy(e => e.Time.Year))
                File.AppendAllLines(Path.Combine(AppSettings.Dir, $"history-{group.Key}.log"), group.Select(Line));
            AppLifecycle.Note($"history files: {missing.Count} entries from the in-app list added");
            return missing.Count;
        }
        catch (Exception ex) { AppLifecycle.Note("history backfill failed: " + ex.Message); return 0; }
    }

    /// <summary>
    /// One line of text: tabs and line breaks become spaces, because the history file keeps one
    /// entry per line with tab-separated fields. Applied when the entry is made, so the list, the
    /// file and the merge key all see the same text.
    /// </summary>
    public static string Plain(string s) => s.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

    public static void Add(ChangeSource src, string detail, string result = "")
    {
        var e = new LogEntry(DateTime.Now, src, Plain(detail), Plain(result));
        lock (_lock)
        {
            _items.AddFirst(e);
            while (_items.Count > Max) _items.RemoveLast();
            SaveNoLock();
            try { File.AppendAllText(HistoryPath, Line(e) + Environment.NewLine); }
            catch { }
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

    /// <summary>
    /// Clears the history the way the owner asked for: the history files of every year and the
    /// in-app list are copied to <c>backups\history-&lt;moment&gt;\</c> in the app folder first,
    /// then the files are deleted and the list emptied. Returns the archive folder, or null (and a
    /// note in the log) when the copy failed, in which case nothing is cleared.
    /// </summary>
    public static string? ClearWithArchive()
    {
        string root = Path.Combine(AppSettings.Dir, "backups");
        string dir = Path.Combine(root, $"history-{DateTime.Now:yyyyMMdd-HHmmss}");
        try
        {
            // a folder of its own every time: two clears within one second must not share one
            // (the second would have overwritten the first copy), so the name gets a suffix
            for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(root, $"history-{DateTime.Now:yyyyMMdd-HHmmss}-{n}");
            Directory.CreateDirectory(dir);
            var files = Directory.Exists(AppSettings.Dir) ? Directory.GetFiles(AppSettings.Dir, "history-*.log").ToList() : new List<string>();
            lock (_lock)
            {
                SaveNoLock();
                // every copy first, never over an existing file; the sources go only after all of them
                if (File.Exists(FilePath)) File.Copy(FilePath, Path.Combine(dir, "changelog.json"), overwrite: false);
                foreach (var f in files) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), overwrite: false);
                foreach (var f in files) File.Delete(f);
                _items.Clear();
                SaveNoLock();
            }
        }
        catch (Exception ex)
        {
            AppLifecycle.Note("history not cleared, the copy failed: " + ex.Message);
            return null;
        }
        Changed?.Invoke();
        return dir;
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
        ChangeSource.Cli          => "CLI",
        ChangeSource.Display      => Lang.T("log_src_display"),
        ChangeSource.Game         => Lang.T("log_src_game"),
        ChangeSource.Restore      => Lang.T("log_src_restore"),
        ChangeSource.Scene        => Lang.T("log_src_scene"),
        ChangeSource.Schedule     => Lang.T("log_src_schedule"),
        ChangeSource.Battery      => Lang.T("log_src_battery"),
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
