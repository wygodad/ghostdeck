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
    IgpuUsage = 262144, CpuName = 524288,
}

public sealed class HotkeyDef
{
    public uint Mods { get; set; }
    public uint Vk { get; set; }
    public string Display { get; set; } = "";
    public bool Enabled { get; set; } = true;   // per-shortcut on/off (discussion #9); default on

    [JsonIgnore] public bool IsSet => Vk != 0;

    // Fields a newer version wrote and this one does not know: kept and written back unchanged
    // (see AppSettings.Extra).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public HotkeyDef Clone() => new() { Mods = Mods, Vk = Vk, Display = Display, Enabled = Enabled, Extra = Extra == null ? null : new(Extra) };
}

public sealed class AppSettings
{
    // Every property of settings.json this version does not know lands here and goes back into
    // the file on the next save, byte for byte. Without it an older version started after a
    // newer one rewrote the file with only the fields it knew, and the newer version's settings
    // (presets, scene fields, switches) were gone when it ran again. Applies from the first
    // version that carries this property on; older versions still drop what they do not know.
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

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
    public bool TrayShowOverlay { get; set; } = true;     // the overlay and its lock entries
    public bool TrayShowCrosshair { get; set; } = true;   // the crosshair entry (a submenu once presets exist)
    public int IconStyle { get; set; } = 2;    // 0=logo, 1=ghost dark tile, 2=ghost light tile (default on new installs), 3=classic gauge, 4=ghost light cyan
    public List<string> IconTabs { get; set; } = new();   // MainTab names shown as strip icons instead of tabs
    public bool ShowGrid { get; set; } = true;             // faint background grid on the pages
    public Dictionary<string, string> Colors { get; set; } = new();   // klucz profilu -> hex
    public bool Autostart { get; set; }
    /// <summary>ghostdeck:// links registered for this user (roadmap #89); off removes the registration.</summary>
    public bool ProtocolLinks { get; set; } = true;

    public bool AutoSwitchEnabled { get; set; } = false;              // domyslnie OFF (nie gryzc sie z MSI)
    public string ProfileOnAC { get; set; } = "Balanced";
    public string ProfileOnBattery { get; set; } = "Silent";

    public int ChargeLimit { get; set; } = 0;                          // 0 = nie zmieniaj; inaczej 20-100
    public int ChargeCustom { get; set; } = 70;                        // ostatnia wlasna wartosc (segment "Wlasny")

    /// <summary>
    /// Czy w ogole zarzadzamy progiem ladowania. Rejestr (0x80 | procent) przyjmuje 10-100, ale
    /// ponizej 20 % limit oznacza laptopa, ktory praktycznie nie laduje baterii - to nie jest
    /// oszczedzanie ogniwa. 60/80/100 to jedyne wartosci potwierdzone na sprzecie (MSI Center
    /// pokazuje tylko je), reszta jest zapisywana tym samym rejestrem - patrz TECHNICAL 69.
    /// </summary>
    public const int ChargeMin = 20, ChargeMax = 100;
    public static bool ChargeManaged(int v) => v is >= ChargeMin and <= ChargeMax;
    public static bool ChargeVerified(int v) => v is 60 or 80 or 100;
    // Ktos inny (MSI Center, jego instalator, BIOS) przestawil prog ladowania w EC - powiadom.
    // Domyslnie WLACZONE: to nie jest alarm o stanie sprzetu, tylko informacja, ze nasze
    // ustawienie przestalo obowiazywac, a bez niej aplikacja pokazuje wartosc, ktorej juz nie ma.
    public bool ChargeExternalNotify { get; set; } = true;

    // ---- Charge-limit travel mode: charge to 100% until the date below, then TravelPrevLimit
    // comes back on its own. MinValue = not active. Any explicit limit change cancels it.
    public DateTime TravelUntil { get; set; } = DateTime.MinValue;
    public int TravelPrevLimit { get; set; }
    public bool StatusOnTop { get; set; } = false;                     // okno Status "zawsze na wierzchu"
    public bool ExperimentalEnabled { get; set; } = false;             // LEGACY pre-1.36 global flag; migrated to ExperimentalWriteFw on startup, never written back as true
    public List<string> ExperimentalWriteFw { get; set; } = new();     // per-model consent: firmware prefixes the owner explicitly allowed writes for

    public bool ExperimentalWriteAllowedFor(string? prefix) =>
        prefix != null && ExperimentalWriteFw.Contains(prefix, StringComparer.OrdinalIgnoreCase);

    // Backup WMI path: firmware labels (EC prefix + BIOS version) on which the path test has
    // passed ON THIS MACHINE. Profile switching through that path is unlocked only for these.
    // Machine-local like LastFirmware: never imported from another machine's settings file.
    public List<string> BackupPathPassed { get; set; } = new();

    public bool BackupPathPassedFor(string? firmware) =>
        !string.IsNullOrEmpty(firmware) && BackupPathPassed.Contains(firmware, StringComparer.OrdinalIgnoreCase);

    // Apex: per-model opt-in for the fourth shift mode (the Extreme recipe then writes the
    // entry's FourthMode value instead of the turbo one). Keyed by firmware prefix, same
    // pattern as the experimental-writes consent. ApexConfirmed = the one-time explainer
    // card was acknowledged once; it is not asked again on other machines.
    public List<string> ApexFw { get; set; } = new();
    // (#293) Single-fan boards where the owner switched on writing the ONE curve into BOTH fan
    // tables (the firmware follows the higher of the two, so the untouched second table is what
    // holds the fan up). Opt-in per model behind a consent card; the vendor's behaviour (table 1
    // only) stays the default. Fan2TableBackup keeps, per prefix, the Fan 2 tables exactly as the
    // controller held them when the switch went on ("t,t,..|s,s,.."), so switching it off puts
    // them back rather than leaving the last mirrored curve there.
    public List<string> BothTablesFw { get; set; } = new();
    public Dictionary<string, string> Fan2TableBackup { get; set; } = new();
    public bool BothTablesEnabledFor(string? prefix) =>
        prefix != null && BothTablesFw.Contains(prefix, StringComparer.OrdinalIgnoreCase);
    public bool ApexConfirmed { get; set; } = false;

    public bool ApexEnabledFor(string? prefix) =>
        prefix != null && ApexFw.Contains(prefix, StringComparer.OrdinalIgnoreCase);

    /// <summary>One-time migration of the legacy global flag: consent narrows to the machine
    /// it was actually given on. Returns true when something changed and needs a save.</summary>
    public bool MigrateExperimentalFlag(string? matchedPrefix, bool deviceIsExperimental)
    {
        if (!ExperimentalEnabled) return false;
        if (deviceIsExperimental && matchedPrefix != null && !ExperimentalWriteAllowedFor(matchedPrefix))
            ExperimentalWriteFw.Add(matchedPrefix);
        ExperimentalEnabled = false;
        return true;
    }

    public bool UpdateCheckEnabled { get; set; } = true;               // raz dziennie sprawdz GitHub Releases (+ ogloszenia)
    public int BackupKeep { get; set; } = 50;                          // ile kopii settings.json trzymac w backups/ (50-150, krok 25)
    public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
    // The model database has its OWN timestamp. It is a static file on the CDN with no request
    // limit, so it can be checked far more often than the release check, which goes to the
    // rate-limited GitHub API. Sharing one field also meant a manual release check silently
    // cancelled the next model-database fetch.
    public DateTime LastModelDbCheckUtc { get; set; } = DateTime.MinValue;
    // Models tab "new since your last visit": first firmware prefix + tier ("17S1IMS1|T") of every
    // model in the database at the previous visit (ModelsPage.UpdateNew).
    public List<string> ModelsSeen { get; set; } = new();
    public List<string> SeenNoticeIds { get; set; } = new();           // ktore ogloszenia (announcements.json) juz pokazano

    public bool DarkMode { get; set; } = true;                         // ciemny motyw domyslnie (brand ghostdeck.dev)

    public string LastFirmware { get; set; } = "";                     // ostatnio widziany firmware EC (ostrzezenie o zmianie)

    // ---- Thermal alert: opt-in OSD and/or GhostDeck card when CPU/GPU stays hot (todo #8) ----
    public bool TempAlertEnabled { get; set; } = false;                // user opts in explicitly
    public int TempAlertDegrees { get; set; } = 90;                    // alert threshold (max of CPU/GPU, °C)
    public int TempAlertSeconds { get; set; } = 10;                    // must stay above threshold this long
    public bool TempAlertOsd { get; set; } = true;                     // the channels (1.38): on-screen toast ...
    public bool TempAlertCard { get; set; } = true;                    // ... and/or a GhostDeck card; both on by default

    // ---- SSD temperature alert: same opt-in pattern. Data comes from Windows storage APIs
    // (Perf.Disks), not the EC, so it works on every machine incl. unrecognised firmware.
    public bool SsdAlertEnabled { get; set; } = false;
    public int SsdAlertDegrees { get; set; } = 70;                     // hottest disk, °C
    public bool SsdAlertOsd { get; set; } = true;
    public bool SsdAlertCard { get; set; } = true;

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

    // ---- Crosshair (roadmap #118, discussion #247) ----
    public bool CrosshairEnabled { get; set; }                          // last visibility, restored at start like the overlay
    public CrosshairStyle Crosshair { get; set; } = new();              // the live look
    public List<CrosshairStyle> CrosshairPresets { get; set; } = new(); // named copies of the look, no limit
    public string? CrosshairActivePreset { get; set; }                  // the preset the look came from (kept while edited; Save writes back)
    // The custom picture travels inside an EXPORTED settings file only: the export fills this
    // from crosshair\custom.png, the import writes it back and clears it. Never saved to
    // settings.json (null is skipped), so the picture is not duplicated on every save.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CrosshairImageBase64 { get; set; }
    // Settings -> Gaming: 0 = the overlay, 1 = the crosshair (machine-local UI state, like SettingsSubTab)
    public int SettingsGamingSub { get; set; }

    // ---- MSI True Color (roadmap #119, discussions #150 and #204) - experimental, opt-in ----
    public bool TrueColorEnabled { get; set; }                          // the switch under Settings -> System
    public bool TrueColorConfirmed { get; set; }                        // the one-time consent card was acknowledged (not asked again)
    public string? TrueColorUserVerified { get; set; }                  // a True Color version the owner confirmed with the in-app test ON THIS MACHINE; not exported
    public int? TrueColorLast { get; set; }                             // the profile GhostDeck last asked for: its own memory, shown as such, not True Color's state
    public DateTime TrueColorLastUtc { get; set; } = DateTime.MinValue;
    public bool FpsMetricSeeded { get; set; }                          // one-time: dosianie metryki FPS istniejacym uzytkownikom (bitmaska sprzed v1.23)

    // ---- game-session report (Status -> Gaming + popup) ----
    public bool SessionPopupEnabled { get; set; } = true;              // pokaz okienko podsumowania po zamknieciu gry
    public int SessionPopupSeconds { get; set; } = 60;                 // czas widocznosci; 0 = az do zamkniecia krzyzykiem
    public int GameSessionKeep { get; set; } = 10;                     // ile ostatnich sesji pamietamy (5-50)
    // (#3) low-FPS alert: OSD toast when the game in front stays under this many FPS; 0 = off
    public int LowFpsAlert { get; set; }

    // ---- profile restore (EC potrafi sam wskoczyc w Super Battery po wybudzeniu / hibernacji) ----
    public bool RestoreProfileOnResume { get; set; }                   // opt-in: przywroc profil po wznowieniu i przy starcie
    public string LastProfile { get; set; } = "";                      // ostatni profil ustawiony swiadomie (persist dla startu)
    public string StartupProfile { get; set; } = "";                   // (#178) "" = ostatni uzywany; nazwa ProfileId = sztywny profil przy starcie aplikacji

    // (#51) auto-wylaczenie Fan Boost po N SEKUNDACH (0 = bez limitu, jak dotad).
    // UI: presety 30 s / 1 / 2 / 3 / 5 / 10 / 15 min + wlasna wartosc w minutach (do 120).
    public int FanBoostSeconds { get; set; }

    // ---- karta "Zasilanie Windows" (#141; roadmapa #109 + #36) ----
    // (discussion #101) kolejnosc profili widziana przez uzytkownika: klucze profili; pusta = standardowa.
    // Stosowana przez Profiles.SetShown przy wczytaniu, imporcie i zapisie z edytora kolejnosci.
    public List<string> ProfileOrder { get; set; } = new();
    public bool PowerModeSync { get; set; }                                  // tryb zasilania Windows podaza za profilem
    public Dictionary<string, int[]> TurboSnapshots { get; set; } = new();   // GUID planu -> [AC, DC] sprzed wylaczenia turbo (zapis planu jest trwaly)
    public long BoostAttrOriginal { get; set; } = -1;                        // pelny DWORD atrybutow PERFBOOSTMODE sprzed odkrycia w Panelu; -1 = nietkniete
    public Dictionary<string, int> PowerModeMap { get; set; } = new();        // wlasne mapowanie profil -> tryb zasilania (0 efektywnosc, 1 zrownowazony, 2 wydajnosc); tylko wiersze ROZNE od domyslnych
    public Dictionary<string, int[]> TurboOriginals { get; set; } = new();  // (#117) GUID planu -> [AC, DC] sprzed PIERWSZEGO zapisu turbo przez GhostDeck; Przywroc wpisuje to
    public Dictionary<string, int[]> TurboMap { get; set; } = new();        // (#117) profil -> [AC, DC] tryb turbo przy przelaczeniu profilu; -1 = bez zmian; tylko profile z czyms ustawionym
    public string PowerModePrev { get; set; } = "";                         // GUID trybu zasilania SPRZED pierwszej zmiany przez GhostDeck ("" = nie zmienialismy); Przywroc go cofa

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

    // Fan-curve page: last view (0 chart, 1 equalizer, 2 deck, 3 playground) + the chart-view
    // options. Machine-local UI state like SettingsSubTab (not imported, not cloned).
    public int FanCurveView { get; set; }
    public bool FanCurveZones { get; set; } = true;      // audibility bands behind the plot
    public bool FanCurveTrail { get; set; }              // 3-minute temperature trail on the curve (opt-in)
    public bool FanCurveDotLabels { get; set; }          // "In action": values next to every history dot (opt-in)
    public bool FanCurveDiagOpen { get; set; }           // "In action": the diagnostics panel (sweep, two-table test) expanded
    public bool FanCurveIntents { get; set; } = true;    // intent tiles above the charts
    public bool FanCurveCompareBar { get; set; } = true; // comparison-chip row between the preset bar and the tiles
    public bool FanCurveTable { get; set; }              // coupled points table expanded
    public List<string> FanCurveCompare { get; set; } = new();   // preset names drawn as comparison layers

    // (discussion #9) Whether Settings always opens on the Start page instead of returning to
    // wherever it was left. Default false = the behaviour so far.
    public bool SettingsAlwaysStart { get; set; }

    // (discussion #9) Separate temperature icons in the tray - one per reading, because a tray
    // icon at 100 % scaling fits TWO digits. Threshold colours: below Warn = Ok, below Hot =
    // Warn, above = Hot. Off by default. SSD (discussion #150) = the hottest disk.
    public bool TempTrayCpu { get; set; }
    public bool TempTrayGpu { get; set; }
    public bool TempTraySsd { get; set; }
    public int TempTrayWarn { get; set; } = 70;
    public int TempTrayHot { get; set; } = 85;
    // The digits carry a dark outline (TrayIconFactory.TextIcon), which is what lets this
    // lighter, more saturated set stay readable on a light and a dark taskbar alike.
    public string TempTrayColorOk { get; set; } = "#3DE3FF";   // brand cyan
    public string TempTrayColorWarn { get; set; } = "#F5B301";   // amber
    public string TempTrayColorHot { get; set; } = "#FF4D4F";   // red
    // A mark in the source's own colour says which reading an icon carries, so the icons can be
    // told apart without hovering. Style = TrayMarker (0 none, 1 bar under the digits - default,
    // 2 dot, 3 corner); the colours default to the profile colours of Silent, Extreme and Super
    // Battery, none of which is a threshold colour.
    public int TempTrayMarkStyle { get; set; } = 1;
    public string TempTrayMarkCpu { get; set; } = "#3C7DFF";    // Silent blue
    public string TempTrayMarkGpu { get; set; } = "#FF2F7D";    // Extreme pink
    public string TempTrayMarkSsd { get; set; } = "#61E7A4";    // Super Battery green

    // zapamietana geometria glownego okna (0 = nieustawione -> domyslny rozmiar/center)
    public int WinX { get; set; }
    public int WinY { get; set; }
    public int WinW { get; set; }
    public int WinH { get; set; }
    public bool WinMaximized { get; set; }

    [JsonIgnore]
    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    /// <summary>
    /// Set by a tool or a test harness that builds the app's pages outside the app (the preview
    /// tool, the check harness): every settings, history and backup file then lives in that
    /// folder, never in the owner's. The preview tool overwrote the owner's settings.json twice
    /// on 24.10.2026 before this existed.
    /// </summary>
    [JsonIgnore]
    public static string? DirOverride { get; set; }
    [JsonIgnore]
    public static string Dir => DirOverride ?? Path.Combine(AppData, "GhostDeck");
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
        // A file that cannot be read is NOT silently replaced: its bytes are kept as a copy in the
        // backups folder and the reason goes to errors.log, so the owner can see what happened
        // and put a good copy back (Settings → System → Backup). Before, the app started with
        // defaults without a word and the next save overwrote the file (24.10.2026).
        string? why = null;
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null) { s.EnsureDefaults(); Profiles.SetShown(s.ProfileOrder); return s; }
                why = "the file holds no settings object";
            }
        }
        catch (Exception ex) { why = ex.Message; }
        if (why != null)
        {
            string? kept = SettingsBackups.Take(SettingsBackups.Kind.Unreadable, FilePath);
            AppLifecycle.Note("settings.json could not be read (" + why + "); defaults are in use" +
                              (kept != null ? ", the file was kept as " + kept : ""));
        }
        var def = new AppSettings();
        def.EnsureDefaults();
        return def;
    }

    private static bool _saveFailureNoted;

    /// <summary>
    /// Writes the file through a temporary one and a swap, so a crash, a full disk or a
    /// reboot in the middle of the write leaves either the previous file or the new one,
    /// never a truncated settings.json (which Load would read as "no settings" and replace
    /// with defaults). The previous file is kept as settings.json.bak. A failure is noted in
    /// the log once per run; the app keeps running on what it has in memory.
    /// </summary>
    // Set by EnsureDefaults, which every instance that stands for the owner's settings passes
    // (Load calls it on both of its paths). A bare `new AppSettings()` that some code saves by
    // mistake (a tool building a page on an empty object, a test) would otherwise overwrite the
    // owner's file with defaults, as the preview tool did on 24.10.2026.
    [JsonIgnore]
    private bool _initialised;

    public void Save()
    {
        if (!_initialised)
        {
            AppLifecycle.Note("settings not saved: this settings object was never initialised (a tool or test instance, not the app's)");
            return;
        }
        try
        {
            Directory.CreateDirectory(Dir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, FilePath + ".bak", ignoreMetadataErrors: true);
            else File.Move(tmp, FilePath);
        }
        catch (Exception ex)
        {
            if (!_saveFailureNoted) { _saveFailureNoted = true; AppLifecycle.Note("settings could not be saved: " + ex.Message); }
        }
    }

    /// <summary>
    /// Why <paramref name="src"/> cannot be imported, or null when it can: every collection the
    /// import copies has to be there (a file with "Hotkeys": null passes the shape check and used
    /// to throw half-way through ImportFrom, after the language and the hotkeys were already
    /// changed), and no list or dictionary may carry a null entry.
    /// </summary>
    public static string? Validate(AppSettings src)
    {
        var nullability = new System.Reflection.NullabilityInfoContext();
        foreach (var prop in typeof(AppSettings).GetProperties())
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
            var pt = prop.PropertyType;
            bool isList = pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(List<>);
            bool isDict = pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(Dictionary<,>);
            if (!isList && !isDict) continue;
            object? value = prop.GetValue(src);
            // a collection declared nullable (List<string>?) may be missing; the others may not
            bool mayBeNull = nullability.Create(prop).ReadState == System.Reflection.NullabilityState.Nullable;
            if (value == null) { if (mayBeNull) continue; return prop.Name + " is missing"; }
            if (isList)
            {
                foreach (object? item in (System.Collections.IEnumerable)value)
                    if (item == null) return prop.Name + " has an empty entry";
            }
            else
            {
                foreach (System.Collections.DictionaryEntry e in (System.Collections.IDictionary)value)
                    if (e.Key == null || e.Value == null) return prop.Name + " has an empty entry";
            }
        }
        if (src.Language == null) return "Language is missing";
        // the picture a backup may carry is part of the file: a bad one refuses the whole import
        if (!string.IsNullOrEmpty(src.CrosshairImageBase64) && CrosshairPainter.ValidateImageBase64(src.CrosshairImageBase64) is { } pic)
            return pic;
        return null;
    }

    /// <summary>
    /// ImportFrom, all or nothing: the file is checked first (<see cref="Validate"/>), then the
    /// import is rehearsed on a copy of these settings, and only an import that went through on
    /// the copy is run on the live object. A file that fails leaves every live setting as it was.
    /// The caller stores the picture the file may carry (CrosshairImageBase64) itself.
    /// </summary>
    public bool TryImportFrom(AppSettings src, out string why)
    {
        why = Validate(src) ?? "";
        if (why.Length > 0) return false;
        try
        {
            var rehearsal = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOpts))
                            ?? throw new InvalidOperationException("settings could not be copied");
            rehearsal.ImportFrom(src);
        }
        catch (Exception ex)
        {
            why = ex.Message;
            Profiles.SetShown(ProfileOrder);   // the rehearsal may have told Profiles about ITS order
            return false;
        }
        ImportFrom(src);
        return true;
    }

    public void EnsureDefaults()
    {
        _initialised = true;   // from here on this instance may be saved (see Save)
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
        DefOff("Mic",      0x7A, "Ctrl+Alt+F11");  // F11 — microphone mute (discussion #231); F10 is the panic reset
        DefOff("Crosshair", 0x7B, "Ctrl+Alt+F12"); // F12 - crosshair on/off (roadmap #118)
        // (#35) FPS snapshot: no key at all by default - every Ctrl+Alt+F-key is taken above and a
        // Ctrl+Alt+letter is an AltGr letter on many layouts; the owner records one in Settings
        if (!Hotkeys.ContainsKey("FpsSnapshot")) Hotkeys["FpsSnapshot"] = new HotkeyDef();

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
        if (SsdAlertDegrees is < 45 or > 90) SsdAlertDegrees = 70;
        if (TravelPrevLimit != 0 && !ChargeManaged(TravelPrevLimit)) TravelPrevLimit = 0;
        if (!ChargeManaged(ChargeCustom)) ChargeCustom = 70;
        if (OsdSeconds is < 1 or > 15) OsdSeconds = 3;
        if (SessionPopupSeconds is < 0 or > 600) SessionPopupSeconds = 60;   // 0 = until closed
        if (GameSessionKeep is < 5 or > 50) GameSessionKeep = 10;
        if (LowFpsAlert is < 0 or > 1000) LowFpsAlert = 0;
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
            if (s.ChargeLimit is { } cl && cl != 0 && !ChargeManaged(cl)) s.ChargeLimit = null;
            if (s.RefreshHz is { } hz && hz is < 0 or > 1000) s.RefreshHz = null;
            if (s.RefreshHz is null) s.RefreshTarget = null;   // the identity travels with the rate
            if (s.BrightnessPct is { } bp && bp is < 0 or > 100) s.BrightnessPct = null;
            if (s.Profile is { } p && !Enum.TryParse<ProfileId>(p, out _)) s.Profile = null;
            if (s.Crosshair is null or false) s.CrosshairPreset = null;   // a preset only goes with "on"
            if (s.TrueColor is { } tc && !TrueColor.IsProfile(tc)) s.TrueColor = null;   // only the seven ids True Color records
        }
        if (TrueColorLast is { } tcl && !TrueColor.IsProfile(tcl)) TrueColorLast = null;
        // Crosshair sanity: every value in range, presets named and unique, a scene's preset
        // that no longer exists falls back to "on with the current look".
        Crosshair ??= new CrosshairStyle();
        Crosshair.Normalize();
        CrosshairPresets.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name));
        var seenCh = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CrosshairPresets.RemoveAll(p => !seenCh.Add(p.Name.Trim()));
        foreach (var p in CrosshairPresets) { p.Name = p.Name.Trim(); p.Normalize(); }
        foreach (var s in Scenes)
            if (s.CrosshairPreset is { } cp && FindCrosshairPreset(cp) == null) s.CrosshairPreset = null;
        if (CrosshairActivePreset is { } acp && FindCrosshairPreset(acp) == null) CrosshairActivePreset = null;
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
        if (TempTrayMarkStyle is < 0 or > 3) TempTrayMarkStyle = 1;
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
        TrayShowOverlay = src.TrayShowOverlay; TrayShowCrosshair = src.TrayShowCrosshair;
        TrayClickLeft = src.TrayClickLeft; TrayClickMiddle = src.TrayClickMiddle; TrayWheelMode = src.TrayWheelMode;
        IconStyle = src.IconStyle;
        IconTabs = new List<string>(src.IconTabs);
        ShowGrid = src.ShowGrid;
        Autostart = src.Autostart;
        ProtocolLinks = src.ProtocolLinks;
        AutoSwitchEnabled = src.AutoSwitchEnabled;
        ProfileOnAC = src.ProfileOnAC;
        ProfileOnBattery = src.ProfileOnBattery;
        ChargeLimit = src.ChargeLimit;
        // Travel mode is machine state (this battery, this trip): the import adopts the file's
        // charge limit, so a revert pending from before the import no longer applies.
        TravelUntil = DateTime.MinValue;
        TravelPrevLimit = 0;
        StatusOnTop = src.StatusOnTop;
        ExperimentalEnabled = src.ExperimentalEnabled;
        ExperimentalWriteFw = new List<string>(src.ExperimentalWriteFw ?? new());
        ApexFw = new List<string>(src.ApexFw ?? new());
        ApexConfirmed = src.ApexConfirmed;
        BothTablesFw = new List<string>(src.BothTablesFw ?? new());
        Fan2TableBackup = new Dictionary<string, string>(src.Fan2TableBackup ?? new());
        // True Color: the switch and the consent travel; the owner-verified version and the last
        // profile are this machine's (another laptop has another True Color)
        TrueColorEnabled = src.TrueColorEnabled;
        TrueColorConfirmed = src.TrueColorConfirmed;
        UpdateCheckEnabled = src.UpdateCheckEnabled;
        DarkMode = src.DarkMode;
        TempAlertEnabled = src.TempAlertEnabled;
        TempAlertDegrees = src.TempAlertDegrees;
        TempAlertSeconds = src.TempAlertSeconds;
        TempAlertOsd = src.TempAlertOsd;
        TempAlertCard = src.TempAlertCard;
        SsdAlertEnabled = src.SsdAlertEnabled;
        SsdAlertDegrees = src.SsdAlertDegrees;
        SsdAlertOsd = src.SsdAlertOsd;
        SsdAlertCard = src.SsdAlertCard;
        OsdSeconds = src.OsdSeconds;
        RefreshSwitchEnabled = src.RefreshSwitchEnabled;
        RefreshOnAC = src.RefreshOnAC;
        RefreshOnBattery = src.RefreshOnBattery;
        BackupKeep = src.BackupKeep;
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
        LowFpsAlert = src.LowFpsAlert;
        RestoreProfileOnResume = src.RestoreProfileOnResume;
        StartupProfile = src.StartupProfile;
        RestoreCurveOnResume = src.RestoreCurveOnResume;   // preferencja tak; sama krzywa (Curve*) zostaje lokalna
        FanBoostSeconds = src.FanBoostSeconds;
        PowerModeSync = src.PowerModeSync;
        PowerModePrev = src.PowerModePrev;
        PowerModeMap = new Dictionary<string, int>(src.PowerModeMap);
        TurboMap = src.TurboMap.Where(kv => kv.Value is { Length: 2 }).ToDictionary(kv => kv.Key, kv => (int[])kv.Value.Clone());
        ProfileOrder = new List<string>(src.ProfileOrder);
        Profiles.SetShown(ProfileOrder);
        // Turbo snapshots and the attribute original are MACHINE state (this Windows, its power
        // plans), not preferences - an imported file must not overwrite what this machine saved.
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
        TempTrayCpu = src.TempTrayCpu; TempTrayGpu = src.TempTrayGpu; TempTraySsd = src.TempTraySsd;
        TempTrayWarn = src.TempTrayWarn; TempTrayHot = src.TempTrayHot;
        TempTrayColorOk = src.TempTrayColorOk; TempTrayColorWarn = src.TempTrayColorWarn;
        TempTrayColorHot = src.TempTrayColorHot;
        TempTrayMarkStyle = src.TempTrayMarkStyle; TempTrayMarkCpu = src.TempTrayMarkCpu;
        TempTrayMarkGpu = src.TempTrayMarkGpu; TempTrayMarkSsd = src.TempTrayMarkSsd;
        CrosshairEnabled = src.CrosshairEnabled;
        Crosshair = (src.Crosshair ?? new CrosshairStyle()).Clone();
        CrosshairPresets.Clear();
        foreach (var p in src.CrosshairPresets) CrosshairPresets.Add(p.Clone());
        CrosshairActivePreset = src.CrosshairActivePreset;
        // The picture the file may carry is NOT written here: this method also runs as a rehearsal
        // on a copy (TryImportFrom), and a file write has no place in it. The caller stores it
        // through CrosshairPainter.ImportImageBytes, with the same size limits as a normal import.
        CrosshairImageBase64 = null;
        EnsureDefaults();
    }

    public CrosshairStyle? FindCrosshairPreset(string name) =>
        CrosshairPresets.FirstOrDefault(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

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
            ProtocolLinks = ProtocolLinks,
            AutoSwitchEnabled = AutoSwitchEnabled,
            ProfileOnAC = ProfileOnAC,
            ProfileOnBattery = ProfileOnBattery,
            ChargeLimit = ChargeLimit,
            StatusOnTop = StatusOnTop,
            ExperimentalEnabled = ExperimentalEnabled,
            ExperimentalWriteFw = new List<string>(ExperimentalWriteFw),
            BackupPathPassed = new List<string>(BackupPathPassed),
            ApexFw = new List<string>(ApexFw),
            ApexConfirmed = ApexConfirmed,
            BothTablesFw = new List<string>(BothTablesFw),
            Fan2TableBackup = new Dictionary<string, string>(Fan2TableBackup),
            TrueColorEnabled = TrueColorEnabled,
            TrueColorConfirmed = TrueColorConfirmed,
            UpdateCheckEnabled = UpdateCheckEnabled,
            HotkeysEnabled = HotkeysEnabled,
            TrayShowStatus = TrayShowStatus, TrayShowFanCurve = TrayShowFanCurve, TrayShowModels = TrayShowModels,
            TrayShowReport = TrayShowReport, TrayShowChangeLog = TrayShowChangeLog,
            TrayShowFeedback = TrayShowFeedback,
            TrayShowOverlay = TrayShowOverlay, TrayShowCrosshair = TrayShowCrosshair,
            TrayClickLeft = TrayClickLeft, TrayClickMiddle = TrayClickMiddle, TrayWheelMode = TrayWheelMode,
            IconStyle = IconStyle,
            IconTabs = new List<string>(IconTabs),
            ShowGrid = ShowGrid,
            LastUpdateCheckUtc = LastUpdateCheckUtc,
            LastModelDbCheckUtc = LastModelDbCheckUtc,
            SeenNoticeIds = new List<string>(SeenNoticeIds),
            DarkMode = DarkMode,
            LastFirmware = LastFirmware,
            TempAlertEnabled = TempAlertEnabled,
            TempAlertDegrees = TempAlertDegrees,
            TempAlertSeconds = TempAlertSeconds,
            TempAlertOsd = TempAlertOsd,
            TempAlertCard = TempAlertCard,
            SsdAlertEnabled = SsdAlertEnabled,
            SsdAlertDegrees = SsdAlertDegrees,
            SsdAlertOsd = SsdAlertOsd,
            SsdAlertCard = SsdAlertCard,
            OsdSeconds = OsdSeconds,
            TravelUntil = TravelUntil,
            TravelPrevLimit = TravelPrevLimit,
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
            LowFpsAlert = LowFpsAlert,
            RestoreProfileOnResume = RestoreProfileOnResume,
            StartupProfile = StartupProfile,
            RestoreCurveOnResume = RestoreCurveOnResume,
            FanBoostSeconds = FanBoostSeconds,
            PowerModeSync = PowerModeSync,
            PowerModePrev = PowerModePrev,
            BoostAttrOriginal = BoostAttrOriginal,
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
        foreach (var (k, v) in TurboSnapshots) c.TurboSnapshots[k] = (int[])v.Clone();
        foreach (var (k, v) in PowerModeMap) c.PowerModeMap[k] = v;
        foreach (var (k, v) in TurboOriginals) c.TurboOriginals[k] = (int[])v.Clone();
        foreach (var (k, v) in TurboMap) c.TurboMap[k] = (int[])v.Clone();
        c.ProfileOrder = new List<string>(ProfileOrder);
        c.ScenHidden = new List<string>(ScenHidden);
        c.SettingsAlwaysStart = SettingsAlwaysStart;
        c.TempTrayCpu = TempTrayCpu; c.TempTrayGpu = TempTrayGpu; c.TempTraySsd = TempTraySsd;
        c.TempTrayWarn = TempTrayWarn; c.TempTrayHot = TempTrayHot;
        c.TempTrayColorOk = TempTrayColorOk; c.TempTrayColorWarn = TempTrayColorWarn;
        c.TempTrayColorHot = TempTrayColorHot;
        c.TempTrayMarkStyle = TempTrayMarkStyle; c.TempTrayMarkCpu = TempTrayMarkCpu;
        c.TempTrayMarkGpu = TempTrayMarkGpu; c.TempTrayMarkSsd = TempTrayMarkSsd;
        c.CrosshairEnabled = CrosshairEnabled;
        c.Crosshair = Crosshair.Clone();
        foreach (var p in CrosshairPresets) c.CrosshairPresets.Add(p.Clone());
        c.CrosshairActivePreset = CrosshairActivePreset;
        c.SettingsGamingSub = SettingsGamingSub;
        c.TrueColorEnabled = TrueColorEnabled; c.TrueColorConfirmed = TrueColorConfirmed;
        c.TrueColorUserVerified = TrueColorUserVerified; c.TrueColorLast = TrueColorLast; c.TrueColorLastUtc = TrueColorLastUtc;
        return c;
    }
}
