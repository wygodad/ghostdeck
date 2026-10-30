using System.Globalization;
using System.IO;
using System.Text;

namespace GhostDeck;

/// <summary>
/// The "FPS snapshot" shortcut (#35): a small text file with the last 30 s of the foreground
/// game (FPS now / average / 1% low, median and 95th-percentile frametime, stutters) next to the
/// hardware state at that moment (temperatures, fans, profile). Saved where the report wizards
/// save theirs (the Desktop, else Documents), so the file sits next to the other reports.
/// The labels are fixed English, like the session export, so files from different machines and
/// languages read the same; only the "not measured" note follows the interface language.
/// </summary>
public static class FpsSnapshotReport
{
    public const int WindowSeconds = 30;

    /// <summary>What the tray collected for one snapshot (FPS part on the UI thread, hardware off it).</summary>
    public sealed record Input(
        DateTime Time, bool Measuring, FpsSnapshot? Current, string Target,
        IReadOnlyList<(DateTime Time, float Ms)> Frames,
        HwSnapshot? Hw, string Profile, string Model, string AppVersion);

    /// <summary>Folder of the user-facing reports: the Desktop, or Documents when there is none.</summary>
    public static string ReportDir()
    {
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return dir;
    }

    /// <summary>True when the input carries frames of a game (FPS part of the report present).</summary>
    public static bool HasFps(Input i) => i.Measuring && i.Frames.Count >= 2;

    public static string Build(Input i)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        void Row(string label, string value) => sb.Append(label.PadRight(20)).AppendLine(value);

        sb.AppendLine("GhostDeck FPS snapshot");
        Row("Time:", i.Time.ToString("yyyy-MM-dd HH:mm:ss", inv));
        if (i.Model.Length > 0) Row("Laptop:", i.Model);
        Row("GhostDeck:", i.AppVersion);
        sb.AppendLine();

        if (!i.Measuring)
        {
            Row("FPS:", "not measured");
            Row("Note:", Lang.T("fps_snap_off"));
        }
        else if (!HasFps(i))
        {
            Row("FPS:", "no game presenting frames");
            Row("Note:", Lang.T("fps_snap_nogame"));
        }
        else
        {
            var ms = i.Frames.Select(f => f.Ms).ToArray();
            Array.Sort(ms);
            int n = ms.Length;
            double mean = ms.Average();
            float median = ms[n / 2];
            float p95 = ms[Math.Min(n - 1, (int)(n * 0.95))];
            float stutterAt = Math.Max(25f, 2f * median);
            int stutters = ms.Count(v => v > stutterAt);
            double span = (i.Frames[^1].Time - i.Frames[0].Time).TotalSeconds;

            Row("Game:", i.Current is { } c ? $"{c.Process} (PID {c.Pid})" : i.Target);
            Row("Window:", $"last {Math.Max(1, (int)Math.Round(span))} s, {n} frames");
            Row("FPS now:", i.Current is { Fps: > 0 } cur ? cur.Fps.ToString(inv) : "-");
            Row("FPS average:", mean > 0 ? Math.Round(1000.0 / mean).ToString(inv) : "-");
            // same rule as everywhere else: 99th-percentile frametime, left out under 100 frames
            Row("1% low:", n >= 100 ? Math.Round(1000.0 / ms[Math.Min(n - 1, (int)(n * 0.99))]).ToString(inv) + " FPS" : "-");
            Row("Median frametime:", median.ToString("0.0", inv) + " ms");
            Row("95% of frames:", "<= " + p95.ToString("0.0", inv) + " ms");
            Row("Stutters:", $"{stutters} (frames over {stutterAt.ToString("0.0", inv)} ms)");
        }
        sb.AppendLine();

        if (i.Hw is { } hw && (hw.CpuTemp > 0 || hw.GpuTemp > 0))
        {
            Row("CPU:", Hw(hw.CpuTemp, hw.CpuRpm, hw.CpuFan));
            Row("GPU:", Hw(hw.GpuTemp, hw.GpuRpm, hw.GpuFan));
        }
        else Row("Hardware:", "not available on this laptop");
        Row("Profile:", i.Profile.Length > 0 ? i.Profile : "-");
        return sb.ToString();

        static string Hw(int temp, int rpm, int pct)
        {
            var parts = new List<string> { temp > 0 ? temp + " °C" : "- °C" };
            if (rpm > 0) parts.Add(rpm + " RPM");
            if (pct > 0) parts.Add(pct + " % fan");
            return string.Join(", ", parts);
        }
    }

    /// <summary>Write the report; returns the full path. Throws when the folder is not writable.</summary>
    public static string Save(string text, string game, DateTime time)
    {
        string name = game.Length > 0 ? game : "no-game";
        foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
        string path = Path.Combine(ReportDir(), $"ghostdeck-fps-snapshot-{name}-{time:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }
}
