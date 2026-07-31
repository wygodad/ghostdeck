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
    public bool? Overlay { get; set; }
    public int? ChargeLimit { get; set; }          // 0 = stop managing, else 60/80/100
    public int? KbdLight { get; set; }             // 0-3 (#26)
    public bool? Webcam { get; set; }              // (#27)
    public bool? FanBoost { get; set; }

    [JsonIgnore] public string HotkeyKey => "Scene:" + Id;

    public SceneDef Clone() => (SceneDef)MemberwiseClone();

    /// <summary>Short human summary of what the scene sets (for the card subtitle).</summary>
    public string Summary()
    {
        var parts = new List<string>();
        if (Profile is { } p) parts.Add(Profiles.All.FirstOrDefault(d => d.Key == p)?.Label ?? p);
        if (CurvePreset is { } c) parts.Add(c.Length == 0 ? Lang.T("fc_preset_auto") : c);
        if (RefreshHz is { } hz) parts.Add(hz + " Hz");
        if (Overlay is { } ov) parts.Add(Lang.T("overlay_title") + " " + Lang.T(ov ? "st_on" : "st_off").ToLowerInvariant());
        if (ChargeLimit is { } cl) parts.Add(cl > 0 ? cl + " %" : Lang.T("st_charge") + " " + Lang.T("st_off").ToLowerInvariant());
        if (KbdLight is { } kl) parts.Add(Lang.T("kbd_title") + " " + Lang.T(kl switch { 0 => "kbd_off", 1 => "kbd_low", 2 => "kbd_mid", _ => "kbd_high" }).ToLowerInvariant());
        if (Webcam is { } wc) parts.Add(Lang.T("webcam_title") + " " + Lang.T(wc ? "st_on" : "st_off").ToLowerInvariant());
        if (FanBoost is { } fb) parts.Add(Lang.T("cooler_boost") + " " + Lang.T(fb ? "st_on" : "st_off").ToLowerInvariant());
        return parts.Count == 0 ? Lang.T("scene_empty_def") : string.Join("  ·  ", parts);
    }
}
