using System.Drawing;

namespace MSIProfileSwitcher;

public enum ProfileId { Silent, Balanced, Extreme, SuperBattery }

/// <summary>UI-level profile definition (model-agnostic). EC recipes live in <see cref="DeviceProfile"/>.</summary>
public sealed record ProfileDef(
    ProfileId Id, string Key, string Label, string SubKey, Color DefaultColor);

public static class Profiles
{
    public static readonly ProfileDef[] All =
    {
        new(ProfileId.Silent,       "Silent",       "SILENT",        "sub_silent",       ColorTranslator.FromHtml("#8B5CF6")),
        new(ProfileId.Balanced,     "Balanced",     "BALANCED",      "sub_balanced",     ColorTranslator.FromHtml("#2D7FF0")),
        new(ProfileId.Extreme,      "Extreme",      "EXTREME",       "sub_extreme",      ColorTranslator.FromHtml("#E0533D")),
        new(ProfileId.SuperBattery, "SuperBattery", "SUPER BATTERY", "sub_superbattery", ColorTranslator.FromHtml("#3FB950")),
    };

    public static readonly ProfileId[] Order =
        { ProfileId.Silent, ProfileId.Balanced, ProfileId.Extreme, ProfileId.SuperBattery };

    public static ProfileDef Get(ProfileId id) => All.First(p => p.Id == id);

    public static readonly string[] Palette =
    {
        "#8B5CF6", "#2D7FF0", "#17C0EB", "#1FB58F", "#3FB950", "#A8CC2C",
        "#F2C037", "#F5871F", "#E0533D", "#E64980", "#B86BFF", "#8895A7",
    };
}
