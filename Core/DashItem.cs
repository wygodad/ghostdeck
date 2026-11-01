using System.Text.Json;
using System.Text.Json.Serialization;

namespace GhostDeck;

/// <summary>
/// One widget on the Dashboard as settings.json stores it: its type and its size in grid cells.
/// The layout is the ORDER of these items; the position on the grid is computed from the order
/// and the window width (UI/Dash/DashLayout), never stored, so one layout fits six columns in a
/// wide window and fewer in a narrow one, and dragging a widget is a change of order.
/// </summary>
public sealed class DashItem
{
    /// <summary>Widget type, e.g. "core.cpuTemp". A type this version does not know stays in the layout and paints as a grey field.</summary>
    public string Type { get; set; } = "";
    public int W { get; set; } = 1;   // columns
    public int H { get; set; } = 1;   // rows

    // Fields a newer version wrote (widget options from stage 2) and this one does not know:
    // kept and written back unchanged (see AppSettings.Extra).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public DashItem() { }
    public DashItem(string type, int w, int h) { Type = type; W = w; H = h; }

    public DashItem Clone() => new() { Type = Type, W = W, H = H, Extra = Extra == null ? null : new(Extra) };

    public const int MaxW = 6, MaxH = 4;

    /// <summary>Version of the layout format; 0 in a settings file from before the Dashboard (1.44).</summary>
    public const int FormatVersion = 1;

    /// <summary>
    /// The layout a new user gets (the mock-up: profiles, the two temperatures, the curve,
    /// scenes, quick toggles, memory, the two fans). On six columns it fills three rows exactly.
    /// </summary>
    public static List<DashItem> DefaultLayout() => new()
    {
        new("core.profiles", 4, 1),
        new("core.cpuTemp",  1, 1),
        new("core.gpuTemp",  1, 1),
        new("core.curve",    2, 2),
        new("core.scenes",   2, 1),
        new("core.toggles",  2, 1),
        new("core.memory",   2, 1),
        new("core.cpuFan",   1, 1),
        new("core.gpuFan",   1, 1),
    };

    /// <summary>Drops empty entries and clamps sizes; a hand-edited or damaged file never breaks the page.</summary>
    public static void Sanitize(List<DashItem> items)
    {
        items.RemoveAll(i => i == null || string.IsNullOrWhiteSpace(i.Type));
        foreach (var i in items)
        {
            i.Type = i.Type.Trim();
            i.W = Math.Clamp(i.W, 1, MaxW);
            i.H = Math.Clamp(i.H, 1, MaxH);
        }
    }
}
