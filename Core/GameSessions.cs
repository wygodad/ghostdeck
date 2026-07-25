using System.IO;
using System.Text;
using System.Text.Json;

namespace GhostDeck;

/// <summary>
/// Persistent store of finished game sessions (newest first), shown in the Status → Gaming
/// picker and exportable per session. Kept in a small JSON file next to the settings; the list
/// is trimmed to the user-chosen size (Settings → Notifications, default 10). Also hosts the
/// one true JSON/CSV serialisation of a session, shared by the report popup and the Gaming tab.
/// </summary>
public static class GameSessions
{
    private static readonly List<GameSession> _items = new();
    private static readonly object _lock = new();

    private static string FilePath => Path.Combine(AppSettings.Dir, "sessions.json");

    public static void Add(GameSession s, int keep)
    {
        lock (_lock)
        {
            _items.Insert(0, s);
            Trim(keep);
            SaveNoLock();
        }
    }

    /// <summary>Apply a (possibly lowered) keep-limit chosen in Settings.</summary>
    public static void ApplyLimit(int keep)
    {
        lock (_lock) { if (Trim(keep)) SaveNoLock(); }
    }

    private static bool Trim(int keep)
    {
        keep = Math.Clamp(keep, 1, 200);
        bool changed = _items.Count > keep;
        while (_items.Count > keep) _items.RemoveAt(_items.Count - 1);
        return changed;
    }

    public static IReadOnlyList<GameSession> All()
    {
        lock (_lock) return _items.ToList();
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var arr = JsonSerializer.Deserialize<List<GameSession>>(File.ReadAllText(FilePath));
            if (arr == null) return;
            lock (_lock)
            {
                _items.Clear();
                _items.AddRange(arr);
            }
        }
        catch { }
    }

    private static void SaveNoLock()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items));
        }
        catch { }
    }

    // ---------------- export (shared by the popup and the Gaming tab) ----------------

    public static string SuggestedFileName(GameSession s, string ext)
    {
        string name = s.Process;
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return $"ghostdeck-session-{name}-{s.End:yyyyMMdd-HHmm}.{ext}";
    }

    public static string ToJson(GameSession s) => JsonSerializer.Serialize(new
    {
        game = s.Process,
        start = s.Start.ToString("yyyy-MM-dd'T'HH:mm:ss"),
        end = s.End.ToString("yyyy-MM-dd'T'HH:mm:ss"),
        durationSeconds = (int)(s.End - s.Start).TotalSeconds,
        avgFps = s.AvgFps, minFps = s.MinFps, maxFps = s.MaxFps, p1LowFps = s.P1LowFps,
        frames = s.Frames, stutters = s.Stutters,
        maxCpuTempC = s.MaxCpuTemp, maxGpuTempC = s.MaxGpuTemp,
        avgCpuRpm = s.AvgCpuRpm, avgGpuRpm = s.AvgGpuRpm,
        profile = s.Profile,
        frametimeSparkMs = s.Spark,        // averaged buckets of the session's closing window
        frametimeSparkPeakMs = s.SparkPeak,
    }, new JsonSerializerOptions { WriteIndented = true });

    public static string ToCsv(GameSession s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("game,start,end,duration_s,avg_fps,min_fps,max_fps,p1_low_fps,frames,stutters,max_cpu_temp_c,max_gpu_temp_c,avg_cpu_rpm,avg_gpu_rpm,profile");
        sb.Append(s.Process).Append(',').Append(s.Start.ToString("yyyy-MM-dd HH:mm:ss")).Append(',')
          .Append(s.End.ToString("yyyy-MM-dd HH:mm:ss")).Append(',').Append((int)(s.End - s.Start).TotalSeconds).Append(',')
          .Append(s.AvgFps).Append(',').Append(s.MinFps).Append(',').Append(s.MaxFps).Append(',')
          .Append(s.P1LowFps).Append(',').Append(s.Frames).Append(',').Append(s.Stutters).Append(',')
          .Append(s.MaxCpuTemp).Append(',').Append(s.MaxGpuTemp).Append(',')
          .Append(s.AvgCpuRpm).Append(',').Append(s.AvgGpuRpm).Append(',').Append(s.Profile).AppendLine();
        return sb.ToString();
    }

    /// <summary>File dialog + write, extension decides the format. Returns false when cancelled.</summary>
    public static bool ExportWithDialog(GameSession s, IWin32Window? owner)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json|CSV (*.csv)|*.csv",
            FileName = SuggestedFileName(s, "json"),
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
        bool csv = Path.GetExtension(dlg.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(dlg.FileName, csv ? ToCsv(s) : ToJson(s));
        return true;
    }
}
