using System.Text.Json;
using System.Text.Json.Serialization;

namespace GhostDeck;

/// <summary>
/// (#21) A scene = one-click macro over existing controls: profile + fan-curve preset +
/// refresh rate + overlay + charge limit + keyboard backlight + webcam + Fan Boost.
/// Every field is nullable - null means "leave as is", so a scene only touches what the
/// user actually picked in the editor. Applied by TrayContext.ApplyScene in a fixed order
/// (profile first - its recipe rewrites the fan byte, so the curve must come after).
/// </summary>
public sealed class SceneDef
{
    // Stable identity for the per-scene hotkey ("Scene:<Id>" in AppSettings.Hotkeys);
    // survives renames, so a rebound hotkey follows the scene.
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string Name { get; set; } = "";
    public string Glyph { get; set; } = "";        // optional emoji/char shown on the card

    public string? Profile { get; set; }           // ProfileId name
    public string? CurvePreset { get; set; }       // "" = back to stock profile fans, else preset name
    public int? RefreshHz { get; set; }
    // monitorDevicePath of the display RefreshHz was chosen for (set by the editor); the apply
    // path skips the rate when the resolved target is a DIFFERENT physical display, so a scene
    // saved against an external monitor never retunes the panel. Null (older scenes) = no guard.
    public string? RefreshTarget { get; set; }
    public int? BrightnessPct { get; set; }        // internal-panel brightness 0-100 (WMI)
    public bool? Hdr { get; set; }                 // HDR / advanced color (DisplayConfig)
    public bool? Overlay { get; set; }
    public int? ChargeLimit { get; set; }          // 0 = stop managing, else 60/80/100
    public int? KbdLight { get; set; }             // 0-3 (#26)
    public bool? Webcam { get; set; }              // (#27)
    public bool? WinLock { get; set; }             // software Windows-key lock
    public bool? Touchpad { get; set; }            // devnode-level touchpad switch
    public bool? FanBoost { get; set; }
    public bool? Turbo { get; set; }               // CPU turbo boost in the active Windows plan, both sources
    public bool? Mic { get; set; }                 // default recording device: true = on, false = muted
    public bool? Crosshair { get; set; }           // the crosshair window (roadmap #118)
    public string? CrosshairPreset { get; set; }   // with Crosshair = true: the preset to load first (null = current look)
    public int? TrueColor { get; set; }            // MSI True Color profile id (roadmap #119); null = leave the profile alone

    // Scene fields a newer version added: kept and written back unchanged (see AppSettings.Extra).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore] public string HotkeyKey => "Scene:" + Id;

    public SceneDef Clone() => (SceneDef)MemberwiseClone();

    /// <summary>Short human summary of what the scene sets (for logs and tooltips).</summary>
    public string Summary()
    {
        var parts = SummaryParts();
        return parts.Count == 0 ? Lang.T("scene_empty_def") : string.Join("  ·  ", parts);
    }

    /// <summary>One entry per setting the scene touches - the scene cards render these as chips.</summary>
    public List<string> SummaryParts()
    {
        var parts = new List<string>();
        if (Profile is { } p) parts.Add(Profiles.All.FirstOrDefault(d => d.Key == p)?.Label ?? p);
        if (CurvePreset is { } c) parts.Add(c.Length == 0 ? Lang.T("fc_preset_auto") : c);
        if (Turbo is { } tb) parts.Add(Lang.T("pw_turbo_label") + " " + Lang.T(tb ? "st_on" : "st_off").ToLowerInvariant());
        if (RefreshHz is { } hz) parts.Add(hz + " Hz");
        if (BrightnessPct is { } bp) parts.Add(Lang.T("bri_title") + " " + bp + " %");
        if (Hdr is { } hd) parts.Add("HDR " + Lang.T(hd ? "st_on" : "st_off").ToLowerInvariant());
        if (Overlay is { } ov) parts.Add(Lang.T("overlay_title") + " " + Lang.T(ov ? "st_on" : "st_off").ToLowerInvariant());
        if (Crosshair is { } ch) parts.Add(Lang.T("ch_title") + " " + (ch && CrosshairPreset is { Length: > 0 } cp ? cp : Lang.T(ch ? "st_on" : "st_off").ToLowerInvariant()));
        if (TrueColor is { } tc) parts.Add("True Color " + (GhostDeck.TrueColor.Name(tc) ?? tc.ToString()));
        if (ChargeLimit is { } cl) parts.Add(cl > 0 ? cl + " %" : Lang.T("st_charge") + " " + Lang.T("st_off").ToLowerInvariant());
        if (KbdLight is { } kl) parts.Add(Lang.T("kbd_title") + " " + Lang.T(kl switch { 0 => "kbd_off", 1 => "kbd_low", 2 => "kbd_mid", _ => "kbd_high" }).ToLowerInvariant());
        if (Webcam is { } wc) parts.Add(Lang.T("webcam_title") + " " + Lang.T(wc ? "st_on" : "st_off").ToLowerInvariant());
        if (Mic is { } mc) parts.Add(Lang.T("mic_title") + " " + Lang.T(mc ? "st_on" : "st_off").ToLowerInvariant());
        if (WinLock is { } wl) parts.Add(Lang.T("winlock_title") + " " + Lang.T(wl ? "st_on" : "st_off").ToLowerInvariant());
        if (Touchpad is { } tp) parts.Add(Lang.T("tp_title") + " " + Lang.T(tp ? "st_on" : "st_off").ToLowerInvariant());
        if (FanBoost is { } fb) parts.Add(Lang.T("cooler_boost") + " " + Lang.T(fb ? "st_on" : "st_off").ToLowerInvariant());
        return parts;
    }
}

/// <summary>
/// (roadmap #48) Scenes as a file: one or more scenes written as JSON, read back on this or
/// another computer. The file carries what a scene sets and nothing of the machine it came from:
/// on the way in every scene gets a new id (hotkeys stay with the computer they were bound on),
/// a free name, values in range, and it drops what this computer does not have - a fan-curve or
/// crosshair preset by a name that is not here, the display its refresh rate was picked for.
/// </summary>
public static class SceneFile
{
    public const int MaxScenes = 50;
    public const int MaxBytes = 512 * 1024;

    private sealed class Envelope
    {
        public string App { get; set; } = "";
        public string Kind { get; set; } = "";
        public int Version { get; set; }
        public List<SceneDef> Scenes { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Export(IEnumerable<SceneDef> scenes) =>
        JsonSerializer.Serialize(new Envelope { App = "GhostDeck", Kind = "scenes", Version = 1, Scenes = scenes.Select(s => s.Clone()).ToList() }, Json);

    public static string FileName(IReadOnlyList<SceneDef> scenes)
    {
        if (scenes.Count != 1) return "ghostdeck-scenes.json";
        string safe = string.Concat(scenes[0].Name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')).Trim('-');
        return safe.Length == 0 ? "ghostdeck-scene.json" : $"ghostdeck-scene-{safe}.json";
    }

    /// <summary>
    /// Reads a scenes file against the settings it is going into. Returns the scenes ready to
    /// add (possibly none); null when the text is not a GhostDeck scenes file at all.
    /// </summary>
    public static List<SceneDef>? Import(string text, AppSettings into)
    {
        if (text.Length > MaxBytes) return null;
        Envelope? env;
        try { env = JsonSerializer.Deserialize<Envelope>(text, Json); }
        catch (JsonException) { return null; }
        if (env == null || !string.Equals(env.Kind, "scenes", StringComparison.OrdinalIgnoreCase) || env.Scenes == null) return null;

        var names = new HashSet<string>(into.Scenes.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        var result = new List<SceneDef>();
        foreach (var src in env.Scenes.Take(MaxScenes))
        {
            if (src == null || string.IsNullOrWhiteSpace(src.Name)) continue;
            var s = src.Clone();
            s.Id = Guid.NewGuid().ToString("N")[..8];
            s.Name = s.Name.Trim();
            if (s.Name.Length > 60) s.Name = s.Name[..60];
            if (s.Glyph.Length > 8) s.Glyph = "";
            string name = s.Name;
            for (int n = 2; !names.Add(name); n++) name = $"{s.Name} ({n})";
            s.Name = name;

            // the same ranges AppSettings keeps for its own scenes
            if (s.KbdLight is { } kl && kl is < 0 or > 3) s.KbdLight = null;
            if (s.ChargeLimit is { } cl && cl != 0 && !AppSettings.ChargeManaged(cl)) s.ChargeLimit = null;
            if (s.RefreshHz is { } hz && hz is < 0 or > 1000) s.RefreshHz = null;
            if (s.BrightnessPct is { } bp && bp is < 0 or > 100) s.BrightnessPct = null;
            if (s.Profile is { } p && !Enum.TryParse<ProfileId>(p, out _)) s.Profile = null;
            if (s.TrueColor is { } tc && !TrueColor.IsProfile(tc)) s.TrueColor = null;
            // what belongs to the computer the file came from
            s.RefreshTarget = null;
            if (s.CurvePreset is { Length: > 0 } cp && into.FindPreset(cp) == null) s.CurvePreset = null;
            if (s.Crosshair is null or false) s.CrosshairPreset = null;
            else if (s.CrosshairPreset is { } chp && into.FindCrosshairPreset(chp) == null) s.CrosshairPreset = null;
            result.Add(s);
        }
        return result;
    }
}
