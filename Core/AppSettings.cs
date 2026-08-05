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
    Fps = 16384, FrameTime = 32768,
    SsdTemp = 65536, BatteryTime = 131072,
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
    // (#23) Tray-icon mouse actions (TrayAction / TrayWheelMode as int).
    public int TrayClickLeft { get; set; } = (int)TrayAction.CycleProfile;
    public int TrayClickMiddle { get; set; } = (int)TrayAction.FanBoost;
    // Default OFF: the wheel-over-the-tray-icon feature needs a system-wide WH_MOUSE_LL hook, so
    // every mouse event in Windows would pass through our hook thread on a fresh install. Opt-in
    // in Settings -> System -> Tray menu. Saved values are untouched (only new installs get None).
    public int TrayWheelMode { get; set; } = (int)GhostDeck.TrayWheelMode.None;

    public bool TrayShowStatus { get; set; } = true;
    public bool TrayShowFanCurve { get; set; } = true;
    public bool TrayShowModels { get; set; } = true;
    public bool TrayShowReport { get; set; } = true;
    public bool TrayShowChangeLog { get; set; } = true;
    public bool TrayShowFeedback { get; set; } = true;
    public int IconStyle { get; set; } = 1;    // 0=logo, 1=ghost dark tile (default), 2=ghost light tile, 3=classic gauge
    public List<string> IconTabs { get; set; } = new();   // MainTab names shown as strip icons instead of tabs
    public bool ShowGrid { get; set; } = true;             // faint background grid on the pages
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

    // ---- Thermal alert: opt-in OSD + tray balloon when CPU/GPU stays hot (todo #8) ----
    public bool TempAlertEnabled { get; set; } = false;                // user opts in explicitly
    public int TempAlertDegrees { get; set; } = 90;                    // alert threshold (max of CPU/GPU, °C)
    public int TempAlertSeconds { get; set; } = 10;                    // must stay above threshold this long

    public int OsdSeconds { get; set; } = 3;                           // how long OSD toasts stay visible (1-15 s)

    // ---- Display refresh-rate auto-switch on AC/battery (discussion #18) ----
    public bool RefreshSwitchEnabled { get; set; } = false;            // opt-in
    public int RefreshOnAC { get; set; } = 0;                          // Hz; 0 = don't change
    public int RefreshOnBattery { get; set; } = 0;                     // Hz; 0 = don't change

    // ---- Fan-curve presets + per-profile assignment ----
    public List<FanCurvePreset> CurvePresets { get; set; } = new();
    // profile key -> preset name ("" / missing = stock fans). Silent is never assignable:
    // its power cap lives in the same EC byte (0xD4) a curve needs, so Silent = stock by design.
    public Dictionary<string, string> ProfileCurves { get; set; } = new();

    public FanCurvePreset? FindPreset(string name) =>
        CurvePresets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

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
        OverlayMetric.CpuRpm | OverlayMetric.GpuRpm | OverlayMetric.Profile | OverlayMetric.CpuLoad | OverlayMetric.Ram |
        OverlayMetric.Fps);
    public bool OverlayBoldText { get; set; } = true;                  // pogrubione etykiety (Segoe UI Semibold) dla czytelnosci przy malej skali
    public bool FpsMetricSeeded { get; set; }                          // one-time: dosianie metryki FPS istniejacym uzytkownikom (bitmaska sprzed v1.23)

    // ---- game-session report (Status -> Gaming + popup) ----
    public bool SessionPopupEnabled { get; set; } = true;              // pokaz okienko podsumowania po zamknieciu gry
    public int SessionPopupSeconds { get; set; } = 60;                 // czas widocznosci; 0 = az do zamkniecia krzyzykiem
    public int GameSessionKeep { get; set; } = 10;                     // ile ostatnich sesji pamietamy (5-50)

    // ---- profile restore (EC potrafi sam wskoczyc w Super Battery po wybudzeniu / hibernacji) ----
    public bool RestoreProfileOnResume { get; set; }                   // opt-in: przywroc profil po wznowieniu i przy starcie
    public string LastProfile { get; set; } = "";                      // ostatni profil ustawiony swiadomie (persist dla startu)

    // (#51) auto-wylaczenie Fan Boost po N SEKUNDACH (0 = bez limitu, jak dotad).
    // UI: presety 30 s / 1 / 2 / 3 / 5 / 10 / 15 min + wlasna wartosc w minutach (do 120).
    public int FanBoostSeconds { get; set; }

    // opt-in (#49): przywroc ostatnia AKTYWNA krzywa wentylatora po starcie/wznowieniu -
    // EC przy zimnym starcie wraca do fabrycznego trybu i gubi kazda wlasna krzywa
    public bool RestoreCurveOnResume { get; set; }
    // ostatnia aktywna krzywa - PUNKTY, nie nazwa presetu (pokrywa tez reczne krzywe z edytora)
    public bool CurveActive { get; set; }
    public string CurveName { get; set; } = "";                        // nazwa presetu do logu ("" = reczna z edytora)
    public int[] CurveCpuTemp { get; set; } = Array.Empty<int>();
    public int[] CurveCpuSpeed { get; set; } = Array.Empty<int>();
    public int[] CurveGpuTemp { get; set; } = Array.Empty<int>();
    public int[] CurveGpuSpeed { get; set; } = Array.Empty<int>();

    /// <summary>Remember the curve that is live in the EC right now (clones the arrays - callers keep editing theirs).</summary>
    public void RecordActiveCurve(string? name, int[] ct, int[] cs, int[] gt, int[] gs)
    {
        CurveActive = true;
        CurveName = name ?? "";
        CurveCpuTemp = (int[])ct.Clone(); CurveCpuSpeed = (int[])cs.Clone();
        CurveGpuTemp = (int[])gt.Clone(); CurveGpuSpeed = (int[])gs.Clone();
        Save();
    }

    /// <summary>The user went back to profile fans (or panic reset) - nothing to restore anymore.</summary>
    public void ClearActiveCurve()
    {
        if (!CurveActive) return;
        CurveActive = false;
        CurveName = "";
        Save();
    }

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

    // (#21) sceny (makra ustawien); kolejnosc listy = kolejnosc kart i pozycji w menu tray
    public List<SceneDef> Scenes { get; set; } = new();

    // Harmonogram scen: reguly czasowe (dni tygodnia + okno godzin -> scena); silnik jest
    // zdarzeniowy (scena wchodzi na POCZATKU okna), pierwsza pasujaca regula wygrywa.
    public bool ScheduleEnabled { get; set; }
    public List<ScheduleRule> Schedules { get; set; } = new();

    // Reguly poziomu baterii: dolna odpala przy przekroczeniu progu W DOL na rozladowaniu,
    // gorna W GORE na ladowaniu; ponowne uzbrojenie 3 p.p. od progu. Akcja: "P:<ProfileId>"
    // albo "S:<sceneId>". BattRulesEnabled = glowny wlacznik calej funkcji.
    public bool BattRulesEnabled { get; set; }
    public bool BattLowEnabled { get; set; }
    public int BattLowPct { get; set; } = 30;
    public string BattLowAction { get; set; } = "P:SuperBattery";
    public bool BattHighEnabled { get; set; }
    public int BattHighPct { get; set; } = 80;
    public string BattHighAction { get; set; } = "P:Balanced";

    // Ukryte elementy zakladki Scenariusze (klucze brickow: fanboost/overlay/charge/autoswitch/
    // kbd/webcam/refresh/panic + "scenes" = cala sekcja scen). Pusta lista = wszystko widoczne.
    public List<string> ScenHidden { get; set; } = new();

    // ostatnio otwarta subzakladka Ustawien (0 = Start/kafelki); wraca po restarcie aplikacji
    public int SettingsSubTab { get; set; }

    // (discussion #9) Whether Settings always opens on the Start page instead of returning to
    // wherever it was left. Default false = the behaviour so far.
    public bool SettingsAlwaysStart { get; set; }

    // (discussion #9) Separate CPU/GPU temperature icons in the tray - two of them, because a
    // tray icon at 100 % scaling fits TWO digits. Threshold colours: below Warn = Ok, below Hot =
    // Warn, above = Hot. Off by default.
    public bool TempTrayCpu { get; set; }
    public bool TempTrayGpu { get; set; }
    public int TempTrayWarn { get; set; } = 70;
    public int TempTrayHot { get; set; } = 85;
    // The digits carry a dark outline (TrayIconFactory.TextIcon), which is what lets this
    // lighter, more saturated set stay readable on a light and a dark taskbar alike.
    public string TempTrayColorOk { get; set; } = "#3DE3FF";   // brand cyan
    public string TempTrayColorWarn { get; set; } = "#F5B301";   // amber
    public string TempTrayColorHot { get; set; } = "#FF4D4F";   // red

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
        Def("PanicReset",   0x79, "Ctrl+Alt+F10");   // 0x79 = F10 — safe-state panic reset
        const uint CS = Hk.MOD_CONTROL | Hk.MOD_SHIFT, WA = Hk.MOD_WIN | Hk.MOD_ALT;
        void DefM(string k, uint mods, uint vk, string disp) { if (!Hotkeys.ContainsKey(k)) Hotkeys[k] = new HotkeyDef { Mods = mods, Vk = vk, Display = disp }; }
        DefM("Overlay",     CS, 0x4F, "Ctrl+Shift+O");   // 0x4F = 'O' — toggle gaming overlay
        DefM("OverlayLock", CS, 0x4C, "Ctrl+Shift+L");   // 0x4C = 'L' — lock/unlock overlay (drag vs click-through)
        DefM("EcView",      CS, 0x45, "Ctrl+Shift+E");   // 0x45 = 'E' — live EC viewer; Ctrl+Shift+T stays the in-window test-tools shortcut

        // (#26/#27) shipped with a binding but DISABLED - visible in Settings, opt-in with one toggle
        void DefOff(string k, uint vk, string disp)
        {
            if (!Hotkeys.ContainsKey(k)) Hotkeys[k] = new HotkeyDef { Mods = CA, Vk = vk, Display = disp, Enabled = false };
        }
        DefOff("KbdLight", 0x75, "Ctrl+Alt+F6");   // F6 — cycle keyboard backlight
        DefOff("Webcam",   0x76, "Ctrl+Alt+F7");   // F7 — webcam switch
        DefOff("WinLock",  0x77, "Ctrl+Alt+F8");   // F8 — Windows-key lock (gaming)
        DefOff("Touchpad", 0x78, "Ctrl+Alt+F9");   // F9 — touchpad on/off (keyboard escape hatch)

        // migrate the earlier dev defaults (Ctrl+Alt+O/G, Win+Alt+G/L) to the new Ctrl+Shift ones
        void MigrateTo(string k, uint vk, string disp, (uint mods, uint vk)[] olds)
        {
            if (Hotkeys.TryGetValue(k, out var h) && olds.Any(o => o.mods == h.Mods && o.vk == h.Vk))
                Hotkeys[k] = new HotkeyDef { Mods = CS, Vk = vk, Display = disp };
        }
        MigrateTo("Overlay", 0x4F, "Ctrl+Shift+O", new[] { (CA, 0x4Fu), (CA, 0x47u), (WA, 0x47u) });
        MigrateTo("OverlayLock", 0x4C, "Ctrl+Shift+L", new[] { (CA, 0x4Cu), (WA, 0x4Cu) });
        // EC viewer briefly shipped on Ctrl+Shift+T, which shadowed the in-window test-tools
        // shortcut (MainForm) globally - move any stored T binding to E.
        MigrateTo("EcView", 0x45, "Ctrl+Shift+E", new[] { (CS, 0x54u) });

        // One-time migration: settings saved before v1.23 carry an OverlayMetrics bitmask without
        // the FPS bit, which would hide the new flagship metric (and never start the monitor from
        // the overlay). Seed it once; the user can still untick it permanently afterwards.
        if (!FpsMetricSeeded)
        {
            OverlayMetrics |= (int)OverlayMetric.Fps;
            FpsMetricSeeded = true;
        }

        // Sanity for hand-edited / imported files: keep the thermal-alert numbers in a sane band.
        if (TempAlertDegrees is < 60 or > 105) TempAlertDegrees = 90;
        if (TempAlertSeconds is < 3 or > 120) TempAlertSeconds = 10;
        if (OsdSeconds is < 1 or > 15) OsdSeconds = 3;
        if (SessionPopupSeconds is < 0 or > 600) SessionPopupSeconds = 60;   // 0 = until closed
        if (GameSessionKeep is < 5 or > 50) GameSessionKeep = 10;
        if (!Enum.IsDefined(typeof(TrayAction), TrayClickLeft)) TrayClickLeft = (int)TrayAction.CycleProfile;
        if (!Enum.IsDefined(typeof(TrayAction), TrayClickMiddle)) TrayClickMiddle = (int)TrayAction.FanBoost;
        if (!Enum.IsDefined(typeof(GhostDeck.TrayWheelMode), TrayWheelMode)) TrayWheelMode = (int)GhostDeck.TrayWheelMode.Profiles;

        // Curve presets sanity (hand-edited / imported files): no nameless or duplicate names,
        // never a Silent assignment (its power cap shares the fan byte), no dangling assignments.
        // (#21) scenes sanity: no nameless scenes, unique ids, sane value ranges, and no
        // orphaned per-scene hotkeys left behind by a deleted scene.
        Scenes.RemoveAll(s => string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.Id));
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Scenes.RemoveAll(s => !seenIds.Add(s.Id));
        foreach (var s in Scenes)
        {
            if (s.KbdLight is { } kl && kl is < 0 or > 3) s.KbdLight = null;
            if (s.ChargeLimit is { } cl && cl is not (0 or 60 or 80 or 100)) s.ChargeLimit = null;
            if (s.RefreshHz is { } hz && hz is < 0 or > 1000) s.RefreshHz = null;
            if (s.BrightnessPct is { } bp && bp is < 0 or > 100) s.BrightnessPct = null;
            if (s.Profile is { } p && !Enum.TryParse<ProfileId>(p, out _)) s.Profile = null;
        }
        foreach (var k in Hotkeys.Keys.Where(k => k.StartsWith("Scene:", StringComparison.OrdinalIgnoreCase)
                                                  && !Scenes.Any(s => s.HotkeyKey.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList())
            Hotkeys.Remove(k);

        // Schedule sanity: unique ids, real times, days mask in range; a rule whose scene was
        // deleted goes away with it (same policy as orphaned scene hotkeys above).
        Schedules.RemoveAll(r => string.IsNullOrWhiteSpace(r.Id));
        var seenSch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Schedules.RemoveAll(r => !seenSch.Add(r.Id));
        Schedules.RemoveAll(r => !Scenes.Any(s => s.Id.Equals(r.SceneId, StringComparison.OrdinalIgnoreCase)));
        foreach (var r in Schedules)
        {
            r.Days &= 0x7F;
            if (ScheduleRule.MinutesOf(r.Start) < 0) r.Start = "08:00";
            if (ScheduleRule.MinutesOf(r.End) < 0) r.End = "16:00";
        }

        // Battery rules sanity: thresholds stay inside 5-95, a broken action string falls back
        // to its default profile; an action pointing at a deleted scene falls back too.
        TempTrayWarn = Math.Clamp(TempTrayWarn, 40, 110);
        TempTrayHot = Math.Clamp(TempTrayHot, TempTrayWarn + 1, 120);
        BattLowPct = Math.Clamp(BattLowPct, 5, 95);
        BattHighPct = Math.Clamp(BattHighPct, 5, 95);
        bool ValidAction(string a) =>
            (a.StartsWith("P:", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<ProfileId>(a[2..], out _)) ||
            (a.StartsWith("S:", StringComparison.OrdinalIgnoreCase) && Scenes.Any(s => s.Id.Equals(a[2..], StringComparison.OrdinalIgnoreCase)));
        if (!ValidAction(BattLowAction)) BattLowAction = "P:SuperBattery";
        if (!ValidAction(BattHighAction)) BattHighAction = "P:Balanced";

        CurvePresets.RemoveAll(p => string.IsNullOrWhiteSpace(p.Name));
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CurvePresets.RemoveAll(p => !seenNames.Add(p.Name.Trim()));
        ProfileCurves.Remove("Silent");
        foreach (var k in ProfileCurves.Keys.ToList())
            if (string.IsNullOrEmpty(ProfileCurves[k]) || FindPreset(ProfileCurves[k]) == null)
                ProfileCurves.Remove(k);
    }

    /// <summary>
    /// Adopt the PREFERENCES from an imported settings file onto this (live) instance — the tray
    /// context and all pages hold references to this object, so it is mutated in place.
    /// Machine-local state is deliberately NOT imported: LastFirmware (the firmware-change guard
    /// must keep judging against THIS machine), the update-check timestamp, seen notice ids and
    /// the window geometry.
    /// </summary>
    public void ImportFrom(AppSettings src)
    {
        Language = src.Language;
        HotkeysEnabled = src.HotkeysEnabled;
        Hotkeys.Clear();
        foreach (var (k, v) in src.Hotkeys) Hotkeys[k] = v.Clone();
        Colors.Clear();
        foreach (var (k, v) in src.Colors) Colors[k] = v;
        TrayShowStatus = src.TrayShowStatus; TrayShowFanCurve = src.TrayShowFanCurve; TrayShowModels = src.TrayShowModels;
        TrayShowReport = src.TrayShowReport; TrayShowChangeLog = src.TrayShowChangeLog;
        TrayShowFeedback = src.TrayShowFeedback;
        TrayClickLeft = src.TrayClickLeft; TrayClickMiddle = src.TrayClickMiddle; TrayWheelMode = src.TrayWheelMode;
        IconStyle = src.IconStyle;
        IconTabs = new List<string>(src.IconTabs);
        ShowGrid = src.ShowGrid;
        Autostart = src.Autostart;
        AutoSwitchEnabled = src.AutoSwitchEnabled;
        ProfileOnAC = src.ProfileOnAC;
        ProfileOnBattery = src.ProfileOnBattery;
        ChargeLimit = src.ChargeLimit;
        StatusOnTop = src.StatusOnTop;
        ExperimentalEnabled = src.ExperimentalEnabled;
        UpdateCheckEnabled = src.UpdateCheckEnabled;
        DarkMode = src.DarkMode;
        TempAlertEnabled = src.TempAlertEnabled;
        TempAlertDegrees = src.TempAlertDegrees;
        TempAlertSeconds = src.TempAlertSeconds;
        OsdSeconds = src.OsdSeconds;
        RefreshSwitchEnabled = src.RefreshSwitchEnabled;
        RefreshOnAC = src.RefreshOnAC;
        RefreshOnBattery = src.RefreshOnBattery;
        OverlayEnabled = src.OverlayEnabled;
        OverlayLayout = src.OverlayLayout;
        OverlayOpacity = src.OverlayOpacity;
        OverlayBgOpacity = src.OverlayBgOpacity;
        OverlayScale = src.OverlayScale;
        OverlayClickThrough = src.OverlayClickThrough;
        OverlayAlwaysTop = src.OverlayAlwaysTop;
        OverlayAccentFromProfile = src.OverlayAccentFromProfile;
        OverlayX = src.OverlayX; OverlayY = src.OverlayY; OverlayMetrics = src.OverlayMetrics;
        OverlayBgEnabled = src.OverlayBgEnabled; OverlayBgColor = src.OverlayBgColor;
        OverlayBoldText = src.OverlayBoldText;
        FpsMetricSeeded = src.FpsMetricSeeded || FpsMetricSeeded;
        SessionPopupEnabled = src.SessionPopupEnabled;
        SessionPopupSeconds = src.SessionPopupSeconds;
        GameSessionKeep = src.GameSessionKeep;
        RestoreProfileOnResume = src.RestoreProfileOnResume;
        RestoreCurveOnResume = src.RestoreCurveOnResume;   // preferencja tak; sama krzywa (Curve*) zostaje lokalna
        FanBoostSeconds = src.FanBoostSeconds;
        ScheduleEnabled = src.ScheduleEnabled;
        Schedules.Clear();
        foreach (var r in src.Schedules) Schedules.Add(r.Clone());
        BattRulesEnabled = src.BattRulesEnabled;
        BattLowEnabled = src.BattLowEnabled; BattLowPct = src.BattLowPct; BattLowAction = src.BattLowAction;
        BattHighEnabled = src.BattHighEnabled; BattHighPct = src.BattHighPct; BattHighAction = src.BattHighAction;
        CurvePresets.Clear();
        foreach (var p in src.CurvePresets) CurvePresets.Add(p.Clone());
        ProfileCurves.Clear();
        foreach (var (k, v) in src.ProfileCurves) ProfileCurves[k] = v;
        Scenes.Clear();
        foreach (var s in src.Scenes) Scenes.Add(s.Clone());   // (#21)
        ScenHidden = new List<string>(src.ScenHidden);
        SettingsAlwaysStart = src.SettingsAlwaysStart;
        TempTrayCpu = src.TempTrayCpu; TempTrayGpu = src.TempTrayGpu;
        TempTrayWarn = src.TempTrayWarn; TempTrayHot = src.TempTrayHot;
        TempTrayColorOk = src.TempTrayColorOk; TempTrayColorWarn = src.TempTrayColorWarn;
        TempTrayColorHot = src.TempTrayColorHot;
        EnsureDefaults();
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
            TrayClickLeft = TrayClickLeft, TrayClickMiddle = TrayClickMiddle, TrayWheelMode = TrayWheelMode,
            IconStyle = IconStyle,
            IconTabs = new List<string>(IconTabs),
            ShowGrid = ShowGrid,
            LastUpdateCheckUtc = LastUpdateCheckUtc,
            SeenNoticeIds = new List<string>(SeenNoticeIds),
            DarkMode = DarkMode,
            LastFirmware = LastFirmware,
            TempAlertEnabled = TempAlertEnabled,
            TempAlertDegrees = TempAlertDegrees,
            TempAlertSeconds = TempAlertSeconds,
            OsdSeconds = OsdSeconds,
            RefreshSwitchEnabled = RefreshSwitchEnabled,
            RefreshOnAC = RefreshOnAC,
            RefreshOnBattery = RefreshOnBattery,
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
            FpsMetricSeeded = FpsMetricSeeded,
            SessionPopupEnabled = SessionPopupEnabled,
            SessionPopupSeconds = SessionPopupSeconds,
            GameSessionKeep = GameSessionKeep,
            RestoreProfileOnResume = RestoreProfileOnResume,
            RestoreCurveOnResume = RestoreCurveOnResume,
            FanBoostSeconds = FanBoostSeconds,
            ScheduleEnabled = ScheduleEnabled,
            BattRulesEnabled = BattRulesEnabled,
            BattLowEnabled = BattLowEnabled, BattLowPct = BattLowPct, BattLowAction = BattLowAction,
            BattHighEnabled = BattHighEnabled, BattHighPct = BattHighPct, BattHighAction = BattHighAction,
            LastProfile = LastProfile,
            WinX = WinX, WinY = WinY, WinW = WinW, WinH = WinH, WinMaximized = WinMaximized,
        };
        foreach (var (k, v) in Hotkeys) c.Hotkeys[k] = v.Clone();
        foreach (var (k, v) in Colors) c.Colors[k] = v;
        foreach (var p in CurvePresets) c.CurvePresets.Add(p.Clone());
        foreach (var (k, v) in ProfileCurves) c.ProfileCurves[k] = v;
        foreach (var s in Scenes) c.Scenes.Add(s.Clone());   // (#21)
        foreach (var r in Schedules) c.Schedules.Add(r.Clone());
        c.ScenHidden = new List<string>(ScenHidden);
        c.SettingsAlwaysStart = SettingsAlwaysStart;
        c.TempTrayCpu = TempTrayCpu; c.TempTrayGpu = TempTrayGpu;
        c.TempTrayWarn = TempTrayWarn; c.TempTrayHot = TempTrayHot;
        c.TempTrayColorOk = TempTrayColorOk; c.TempTrayColorWarn = TempTrayColorWarn;
        c.TempTrayColorHot = TempTrayColorHot;
        return c;
    }
}
