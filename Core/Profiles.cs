using System.Drawing;

namespace GhostDeck;

public enum ProfileId { Silent, Balanced, Extreme, SuperBattery }

/// <summary>UI-level profile definition (model-agnostic). EC recipes live in <see cref="DeviceProfile"/>.</summary>
public sealed record ProfileDef(
    ProfileId Id, string Key, string Label, string SubKey, Color DefaultColor);

public static class Profiles
{
    // Defaults: blue feather, amber scales, pink bolt, green battery (user-approved reference shot).
    public static readonly ProfileDef[] All =
    {
        new(ProfileId.Silent,       "Silent",       "SILENT",        "sub_silent",       ColorTranslator.FromHtml("#3C7DFF")),
        new(ProfileId.Balanced,     "Balanced",     "BALANCED",      "sub_balanced",     ColorTranslator.FromHtml("#FFC15D")),
        new(ProfileId.Extreme,      "Extreme",      "EXTREME",       "sub_extreme",      ColorTranslator.FromHtml("#FF2F7D")),
        new(ProfileId.SuperBattery, "SuperBattery", "SUPER BATTERY", "sub_superbattery", ColorTranslator.FromHtml("#61E7A4")),
    };

    public static readonly ProfileId[] Order =
        { ProfileId.Silent, ProfileId.Balanced, ProfileId.Extreme, ProfileId.SuperBattery };

    public static ProfileDef Get(ProfileId id) => All.First(p => p.Id == id);

    // Swatch palette anchored to the ghostdeck.dev colours; must contain every DefaultColor
    // above so the "selected" marker can point at a default.
    public static readonly string[] Palette =
    {
        "#8D63FF", "#B86BFF", "#3C7DFF", "#3DE3FF", "#1FB58F", "#61E7A4",
        "#A8CC2C", "#FFC15D", "#F5871F", "#E0533D", "#FF2F7D", "#8895A7",
    };
}
