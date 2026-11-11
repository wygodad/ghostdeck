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

    /// <summary>
    /// The widget's own settings (1.45): colour, label, scale, which elements it shows. Absent =
    /// every setting at its default, the look of 1.44. Read through <see cref="DashOptions"/>,
    /// which never trusts a value. 1.44 does not know the field and carries it in
    /// <see cref="Extra"/>, so going back to the older exe keeps the settings.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Opt { get; set; }

    // Fields a newer version wrote and this one does not know: kept and written back unchanged
    // (see AppSettings.Extra).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public DashItem() { }
    public DashItem(string type, int w, int h) { Type = type; W = w; H = h; }

    public DashItem Clone() => new() { Type = Type, W = W, H = H, Opt = Opt == null ? null : new(Opt), Extra = Extra == null ? null : new(Extra) };

    /// <summary>Most settings one widget keeps, and the most characters they take in the file; what is over goes from the end (<see cref="DashOptions.Sanitized"/>), in a layout file a larger map refuses the file.</summary>
    public const int MaxOptKeys = 24, MaxOptChars = 2048;

    public const int MaxW = 6, MaxH = 4;

    /// <summary>
    /// The most widgets one Dashboard holds. A type may stand any number of times (1.45), so this
    /// is no limit on copies; it is the far end that keeps a damaged file from freezing the tab:
    /// every widget is laid out and asked on each tick whether its live part changed, on screen
    /// or not (the picture itself is kept for the visible band only, see DashCanvas), and
    /// thousands of them would take seconds.
    /// </summary>
    public const int MaxItems = 200;

    /// <summary>
    /// The most grid cells the widgets of one Dashboard cover together (width times height,
    /// summed). With <see cref="MaxItems"/> it bounds how much there is to lay out and draw,
    /// whatever a layout file or a settings file says: 80 full rows of a six-column grid, up to
    /// 120 rows of four-wide widgets on four columns. Memory does not hang on it: the canvas keeps
    /// pictures only as large as what the page shows (DashCanvas).
    /// </summary>
    public const int MaxCells = 480;

    /// <summary>The cells a list of items covers.</summary>
    public static int Cells(IEnumerable<DashItem> items) => items.Sum(i => Math.Clamp(i.W, 1, MaxW) * Math.Clamp(i.H, 1, MaxH));

    /// <summary>A widget type's id: lower-case letters, a dot, then letters, digits and underscores; a fixed length at most.</summary>
    public static bool TypeOk(string? type)
    {
        if (string.IsNullOrEmpty(type) || type.Length > 57) return false;
        int dot = type.IndexOf('.');
        if (dot < 1 || dot > 16 || dot == type.Length - 1 || type.Length - dot - 1 > 40) return false;
        for (int i = 0; i < dot; i++) if (type[i] is < 'a' or > 'z') return false;
        for (int i = dot + 1; i < type.Length; i++) if (!char.IsAsciiLetterOrDigit(type[i]) && type[i] != '_') return false;
        return true;
    }

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

    /// <summary>
    /// The size a widget takes when its allowed sizes changed under it (its settings did): the
    /// one it has when that is still allowed, else the nearest allowed one, the earlier in the
    /// list on a tie.
    /// </summary>
    public static (int w, int h) Fit(IReadOnlyList<System.Drawing.Size> sizes, int w, int h)
    {
        if (sizes.Count == 0) return (w, h);
        int best = 0, bestD = int.MaxValue;
        for (int i = 0; i < sizes.Count; i++)
        {
            int d = Math.Abs(sizes[i].Width - w) + Math.Abs(sizes[i].Height - h);
            if (d < bestD) { best = i; bestD = d; }
        }
        return (sizes[best].Width, sizes[best].Height);
    }

    /// <summary>Drops empty entries and clamps sizes; a hand-edited or damaged file never breaks the page.</summary>
    public static void Sanitize(List<DashItem> items)
    {
        items.RemoveAll(i => i == null || string.IsNullOrWhiteSpace(i.Type));
        foreach (var i in items) i.Type = i.Type.Trim();
        items.RemoveAll(i => !TypeOk(i.Type));   // an id of the fixed shape, known or not; anything else is not a widget
        if (items.Count > MaxItems) items.RemoveRange(MaxItems, items.Count - MaxItems);
        int cells = 0;
        for (int n = 0; n < items.Count; n++)
        {
            var i = items[n];
            i.W = Math.Clamp(i.W, 1, MaxW);
            i.H = Math.Clamp(i.H, 1, MaxH);
            cells += i.W * i.H;
            if (cells > MaxCells) { items.RemoveRange(n, items.Count - n); break; }   // the area's bound: what is over it goes from the end
            // the settings, value by value: what cannot be read or does not fit goes, the rest
            // stays (never the whole map for one bad value or one key too many)
            i.Opt = DashOptions.Sanitized(i.Opt, MaxOptKeys, MaxOptChars);
        }
    }
}
