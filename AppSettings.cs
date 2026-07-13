using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GhostDeck;

/// <summary>Which metrics the gaming overlay shows (bit flags, persisted as an int).</summary>
[Flags]
public enum OverlayMetric
{
    None = 0,
    CpuTemp = 1, GpuTemp = 2,
    CpuRpm = 4, GpuRpm = 8,
    Profile = 16, FanPct = 32, CoolerBoost = 64,
    CpuLoad = 128, Ram = 256, ChargeLimit = 512, Battery = 1024,
    GpuUsage = 2048, Vram = 4096, CpuClock = 8192,
}

public sealed class HotkeyDef
{
    public uint Mods { get; set; }
    public uint Vk { get; set; }
    public string Display { get; set; } = "";
    public bool Enabled { get; set; } = true;   // per-shortcut on/off (discussion #9); default on

    [JsonIgnore] public bool IsSet => Vk != 0;

    public HotkeyDef Clone() => new() { Mods = Mods, Vk = Vk, Display = Display, Enabled = Enabled };
}

public sealed class AppSettings
{
    public string Language { get; set; } = "en";
    public Dictionary<string, HotkeyDef> Hotkeys { get; set; } = new();
    public bool HotkeysEnabled { get; set; } = true;   // master on/off for all keyboard shortcuts (#9)

    // Which tray context-menu entries are shown; all default on (discussion #9).
    public bool TrayShowStatus { get; set; } = true;
    public bool TrayShowFanCurve { get; set; } = true;
    public bool TrayShowModels { get; set; } = true;
    public bool TrayShowReport { get; set; } = true;
    public bool TrayShowChangeLog { get; set; } = true;
    public bool TrayShowFeedback { get; set; } = true;
    public int IconStyle { get; set; } = 1;    // 0=logo, 1=ghost dark tile (default), 2=ghost light tile, 3=classic gauge
    public Dictionary<string, string> Colors { get; set; } = new();   // klucz profilu -> hex
    public bool Autostart { get; set; }

    public bool AutoSwitchEnabled { get; set; } = false;              // domyslnie OFF (nie gryzc sie z MSI)
    public string ProfileOnAC { get; set; } = "Balanced";
    public string ProfileOnBattery { get; set; } = "Silent";

    public int ChargeLimit { get; set; } = 0;                          // 0 = nie zmieniaj; inaczej 60/80/100
    public bool StatusOnTop { get; set; } = false;                     // okno Status "zawsze na wierzchu"
    public bool ExperimentalEnabled { get; set; } = false;             // pozwol na zapis dla modeli Experimental

    public bool UpdateCheckEnabled { get; set; } = true;               // raz dziennie sprawdz GitHub Releases (+ ogloszenia)
    public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
    public List<string> SeenNoticeIds { get; set; } = new();           // ktore ogloszenia (announcements.json) juz pokazano

    public bool DarkMode { get; set; } = true;                         // ciemny motyw domyslnie (brand ghostdeck.dev)

    public string LastFirmware { get; set; } = "";                     // ostatnio widziany firmware EC (ostrzezenie o zmianie)

    // ---- Gaming overlay (odczepiany, always-on-top mini-panel) ----
    public bool OverlayEnabled { get; set; } = false;                  // ostatni stan widocznosci (przywracany po starcie)
    public string OverlayLayout { get; set; } = "Card";                // "Card" (pionowa karta) | "Bar" (poziomy pasek)
    public int OverlayOpacity { get; set; } = 95;                      // przezroczystosc TRESCI (napisy+ikony) 40..100 %
    public int OverlayBgOpacity { get; set; } = 82;                    // przezroczystosc TLA 0..100 % (niezalezna od tresci)
    public int OverlayScale { get; set; } = 100;                       // 80..160 %
    public bool OverlayClickThrough { get; set; } = false;             // true = mysz przechodzi do gry (nie mozna przeciagac)
    public bool OverlayAlwaysTop { get; set; } = true;
    public bool OverlayAccentFromProfile { get; set; } = true;         // akcent = kolor aktywnego profilu
    public int OverlayX { get; set; } = -1;                            // -1 => domyslny rog
    public int OverlayY { get; set; } = -1;
    public bool OverlayBgEnabled { get; set; } = true;                 // false = tlo wylaczone (czysty HUD, tylko napisy/ikony)
    public string OverlayBgColor { get; set; } = "#16181D";            // kolor tla nakladki
    public int OverlayMetrics { get; set; } = (int)(OverlayMetric.CpuTemp | OverlayMetric.GpuTemp |
        OverlayMetric.CpuRpm | OverlayMetric.GpuRpm | OverlayMetric.Profile | OverlayMetric.CpuLoad | OverlayMetric.Ram);
    public bool OverlayBoldText { get; set; } = true;                  // pogrubione etykiety (Segoe UI Semibold) dla czytelnosci przy malej skali

    // Reset just the Gaming-overlay settings to their defaults (leaves everything else untouched).
    public void RestoreOverlayDefaults()
    {
        var d = new AppSettings();
        OverlayLayout = d.OverlayLayout; OverlayOpacity = d.OverlayOpacity; OverlayBgOpacity = d.OverlayBgOpacity; OverlayScale = d.OverlayScale;
        OverlayClickThrough = d.OverlayClickThrough; OverlayAlwaysTop = d.OverlayAlwaysTop;
        OverlayAccentFromProfile = d.OverlayAccentFromProfile; OverlayX = d.OverlayX; OverlayY = d.OverlayY;
        OverlayBgEnabled = d.OverlayBgEnabled; OverlayBgColor = d.OverlayBgColor; OverlayMetrics = d.OverlayMetrics;
        OverlayBoldText = d.OverlayBoldText;
    }

    [JsonIgnore] public OverlayMetric Metrics => (OverlayMetric)OverlayMetrics;
    public bool HasMetric(OverlayMetric m) => (OverlayMetrics & (int)m) != 0;
    public void SetMetric(OverlayMetric m, bool on) => OverlayMetrics = on ? OverlayMetrics | (int)m : OverlayMetrics & ~(int)m;

    // zapamietana geometria glownego okna (0 = nieustawione -> domyslny rozmiar/center)
    public int WinX { get; set; }
    public int WinY { get; set; }
    public int WinW { get; set; }
    public int WinH { get; set; }
    public bool WinMaximized { get; set; }

    [JsonIgnore]
    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    [JsonIgnore]
    public static string Dir => Path.Combine(AppData, "GhostDeck");
    [JsonIgnore]
    private static string OldDir => Path.Combine(AppData, "MSIProfileSwitcher");   // pre-rename settings folder
    [JsonIgnore]
    public static string FilePath => Path.Combine(Dir, "settings.json");

    // One-time rename migration: copy settings.json + changelog.json from the old folder into the new
    // one (copy, not move — the old folder is left intact as a backup). No-op once the new folder exists.
    private static void MigrateFromOldDir()
    {
        try
        {
            if (Directory.Exists(Dir) || !Directory.Exists(OldDir)) return;
            Directory.CreateDirectory(Dir);
            foreach (var name in new[] { "settings.json", "changelog.json" })
            {
                var src = Path.Combine(OldDir, name);
                if (File.Exists(src)) File.Copy(src, Path.Combine(Dir, name), overwrite: false);
            }
        }
        catch { }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        MigrateFromOldDir();
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null) { s.EnsureDefaults(); return s; }
            }
        }
        catch { }
        var def = new AppSettings();
        def.EnsureDefaults();
        return def;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { }
    }

    public void EnsureDefaults()
    {
        const uint CA = Hk.MOD_CONTROL | Hk.MOD_ALT;
        void Def(string k, uint vk, string disp)
        {
            if (!Hotkeys.ContainsKey(k))
                Hotkeys[k] = new HotkeyDef { Mods = CA, Vk = vk, Display = disp };
        }
        Def("Silent",       0x70, "Ctrl+Alt+F1");
        Def("Balanced",     0x71, "Ctrl+Alt+F2");
        Def("Extreme",      0x72, "Ctrl+Alt+F3");
        Def("SuperBattery", 0x73, "Ctrl+Alt+F4");
        Def("Cycle",        0x50, "Ctrl+Alt+P");
        Def("CoolerBoost",  0x74, "Ctrl+Alt+F5");
        const uint CS = Hk.MOD_CONTROL | Hk.MOD_SHIFT, WA = Hk.MOD_WIN | Hk.MOD_ALT;
        void DefM(string k, uint mods, uint vk, string disp) { if (!Hotkeys.ContainsKey(k)) Hotkeys[k] = new HotkeyDef { Mods = mods, Vk = vk, Display = disp }; }
        DefM("Overlay",     CS, 0x4F, "Ctrl+Shift+O");   // 0x4F = 'O' — toggle gaming overlay
        DefM("OverlayLock", CS, 0x4C, "Ctrl+Shift+L");   // 0x4C = 'L' — lock/unlock overlay (drag vs click-through)

        // migrate the earlier dev defaults (Ctrl+Alt+O/G, Win+Alt+G/L) to the new Ctrl+Shift ones
        void MigrateTo(string k, uint vk, string disp, (uint mods, uint vk)[] olds)
        {
            if (Hotkeys.TryGetValue(k, out var h) && olds.Any(o => o.mods == h.Mods && o.vk == h.Vk))
                Hotkeys[k] = new HotkeyDef { Mods = CS, Vk = vk, Display = disp };
        }
        MigrateTo("Overlay", 0x4F, "Ctrl+Shift+O", new[] { (CA, 0x4Fu), (CA, 0x47u), (WA, 0x47u) });
        MigrateTo("OverlayLock", 0x4C, "Ctrl+Shift+L", new[] { (CA, 0x4Cu), (WA, 0x4Cu) });
    }

    public Color ColorFor(ProfileId id)
    {
        var def = Profiles.Get(id);
        if (Colors.TryGetValue(def.Key, out var hex) && !string.IsNullOrWhiteSpace(hex))
        {
            try { return ColorTranslator.FromHtml(hex); } catch { }
        }
        return def.DefaultColor;
    }

    public AppSettings Clone()
    {
        var c = new AppSettings
        {
            Language = Language,
            Autostart = Autostart,
            AutoSwitchEnabled = AutoSwitchEnabled,
            ProfileOnAC = ProfileOnAC,
            ProfileOnBattery = ProfileOnBattery,
            ChargeLimit = ChargeLimit,
            StatusOnTop = StatusOnTop,
            ExperimentalEnabled = ExperimentalEnabled,
            UpdateCheckEnabled = UpdateCheckEnabled,
            HotkeysEnabled = HotkeysEnabled,
            TrayShowStatus = TrayShowStatus, TrayShowFanCurve = TrayShowFanCurve, TrayShowModels = TrayShowModels,
            TrayShowReport = TrayShowReport, TrayShowChangeLog = TrayShowChangeLog,
            TrayShowFeedback = TrayShowFeedback,
            IconStyle = IconStyle,
            LastUpdateCheckUtc = LastUpdateCheckUtc,
            SeenNoticeIds = new List<string>(SeenNoticeIds),
            DarkMode = DarkMode,
            LastFirmware = LastFirmware,
            OverlayEnabled = OverlayEnabled,
            OverlayLayout = OverlayLayout,
            OverlayOpacity = OverlayOpacity,
            OverlayBgOpacity = OverlayBgOpacity,
            OverlayScale = OverlayScale,
            OverlayClickThrough = OverlayClickThrough,
            OverlayAlwaysTop = OverlayAlwaysTop,
            OverlayAccentFromProfile = OverlayAccentFromProfile,
            OverlayX = OverlayX, OverlayY = OverlayY, OverlayMetrics = OverlayMetrics,
            OverlayBgEnabled = OverlayBgEnabled, OverlayBgColor = OverlayBgColor,
            OverlayBoldText = OverlayBoldText,
            WinX = WinX, WinY = WinY, WinW = WinW, WinH = WinH, WinMaximized = WinMaximized,
        };
        foreach (var (k, v) in Hotkeys) c.Hotkeys[k] = v.Clone();
        foreach (var (k, v) in Colors) c.Colors[k] = v;
        return c;
    }
}
