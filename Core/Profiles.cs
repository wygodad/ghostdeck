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

    /// <summary>The fixed, canonical order: model database, hotkey numbering, tests. Never changes.</summary>
    public static readonly ProfileId[] Order =
        { ProfileId.Silent, ProfileId.Balanced, ProfileId.Extreme, ProfileId.SuperBattery };

    /// <summary>Super Battery up to Extreme - the "by power" preset of the order editor (discussion #101).</summary>
    public static readonly ProfileId[] ByPower =
        { ProfileId.SuperBattery, ProfileId.Silent, ProfileId.Balanced, ProfileId.Extreme };

    /// <summary>
    /// The order the user sees and cycles through: profile tiles, tray menu, profile lists,
    /// "next profile". Always a NEW array on a change, never edited in place - a list that was
    /// built from an earlier order keeps reading that earlier array and stays consistent with
    /// itself until it is rebuilt.
    /// </summary>
    public static ProfileId[] Shown { get; private set; } = Order;

    /// <summary>
    /// Makes a saved order (profile keys) current. Anything that is not all four profiles
    /// exactly once - an empty list, an old file, a typo - falls back to <see cref="Order"/>.
    /// </summary>
    public static void SetShown(IEnumerable<string>? keys)
    {
        var ids = new List<ProfileId>();
        foreach (var k in keys ?? Enumerable.Empty<string>())
            if (Enum.TryParse<ProfileId>(k, out var id) && Enum.IsDefined(id) && !ids.Contains(id)) ids.Add(id);
        Shown = ids.Count == Order.Length ? ids.ToArray() : Order;
    }

    public static ProfileDef Get(ProfileId id) => All.First(p => p.Id == id);

    // Swatch palette anchored to the ghostdeck.dev colours; must contain every DefaultColor
    // above so the "selected" marker can point at a default.
    public static readonly string[] Palette =
    {
        "#8D63FF", "#B86BFF", "#3C7DFF", "#3DE3FF", "#1FB58F", "#61E7A4",
        "#A8CC2C", "#FFC15D", "#F5871F", "#E0533D", "#FF2F7D", "#8895A7",
    };
}
