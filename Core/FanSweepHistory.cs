using System.IO;
using System.Text.Json;

namespace GhostDeck;

/// <summary>
/// Every fan sweep the user runs is kept, so a result can be compared with an older one (a fan
/// slowing down over months is exactly the kind of thing one run cannot show) and re-exported
/// long after the window was closed. Stored as one JSON file next to the settings, newest
/// first, capped - a sweep is ~1 KB, the cap is about keeping the picker readable.
///
/// The stored shape is the sweep's own result plus the context needed to read it later
/// (firmware, model, app version): a report rebuilt from an entry must say the same as the one
/// copied right after the run.
/// </summary>
public static class FanSweepHistory
{
    public const int Keep = 30;

    public sealed class Entry
    {
        public DateTime When { get; set; }
        public string Model { get; set; } = "";
        public string Firmware { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public bool HasTach { get; set; }
        public bool Aborted { get; set; }
        public double Seconds { get; set; }
        public List<Step> Steps { get; set; } = new();
        /// <summary>The findings as the app worded them when the sweep finished (already localised).</summary>
        public List<string> Findings { get; set; } = new();

        public sealed class Step
        {
            public int Set { get; set; }
            public int CpuDuty { get; set; }
            public int GpuDuty { get; set; }
            public int CpuRpm { get; set; }
            public int GpuRpm { get; set; }
            public int CpuTemp { get; set; }
            public int GpuTemp { get; set; }
            public double Settle { get; set; }
        }

        /// <summary>Back to the runtime result, so the same painter and report code can consume it.</summary>
        public FanSweep.Result ToResult()
        {
            var r = new FanSweep.Result { HasTach = HasTach, Started = When, Aborted = Aborted, Duration = TimeSpan.FromSeconds(Seconds) };
            foreach (var s in Steps)
                r.Steps.Add(new FanSweep.StepResult(s.Set, s.CpuDuty, s.GpuDuty, s.CpuRpm, s.GpuRpm, s.CpuTemp, s.GpuTemp, s.Settle));
            return r;
        }

        /// <summary>
        /// Label for the picker: when the sweep ran, and nothing else. A run is identified by
        /// its time - anything more (step counts, flags) only made the list harder to read.
        /// </summary>
        public string Label() => $"{When:yyyy-MM-dd HH:mm}";
    }

    private static string FilePath => Path.Combine(AppSettings.Dir, "fan-sweeps.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static List<Entry> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? new List<Entry>();
        }
        catch { }
        return new List<Entry>();
    }

    /// <summary>Store a finished sweep at the front of the list; returns the trimmed list.</summary>
    public static List<Entry> Add(FanSweep.Result r, string model, string firmware, string appVersion, IEnumerable<string> findings)
    {
        var list = Load();
        var e = new Entry
        {
            When = r.Started,
            Model = model,
            Firmware = firmware,
            AppVersion = appVersion,
            HasTach = r.HasTach,
            Aborted = r.Aborted,
            Seconds = r.Duration.TotalSeconds,
            Findings = findings.ToList(),
        };
        foreach (var s in r.Steps)
            e.Steps.Add(new Entry.Step
            {
                Set = s.DutyPct, CpuDuty = s.CpuDuty, GpuDuty = s.GpuDuty,
                CpuRpm = s.CpuRpm, GpuRpm = s.GpuRpm, CpuTemp = s.CpuTemp, GpuTemp = s.GpuTemp,
                Settle = s.SecondsToSettle,
            });
        list.Insert(0, e);
        if (list.Count > Keep) list.RemoveRange(Keep, list.Count - Keep);
        Save(list);
        return list;
    }

    public static void Save(List<Entry> list)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list, JsonOpts));
        }
        catch { }
    }
}
