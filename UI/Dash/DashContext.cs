namespace GhostDeck;

/// <summary>
/// What a widget gets from the canvas, and nothing more: the shared data sample of the current
/// tick, the actions of <see cref="MainDeps"/> (profile, scene, Fan Boost, switches, charge
/// limit), the scale, the theme, the language and a way to open another tab. One instance per
/// canvas; the sample is replaced on every tick.
/// </summary>
internal sealed class DashContext
{
    private readonly Dictionary<(float em, FontStyle style), Font> _fonts = new();
    private readonly Dictionary<(int px, FontStyle style), Font> _pxFonts = new();

    public DashContext(MainDeps d, Control owner) { D = d; Owner = owner; }

    /// <summary>Data and actions of the application. Writes go through its actions only.</summary>
    public MainDeps D { get; }

    /// <summary>The canvas; the scale is read from it (Dpi.Of), never from a cached value.</summary>
    public Control Owner { get; }

    /// <summary>The sample of the current tick (empty until the first read).</summary>
    public DashSample Sample { get; internal set; } = DashSample.Empty;

    /// <summary>Asks the canvas to render the static layer again (a widget finished loading data it paints there).</summary>
    public Action RequestRebuild { get; internal set; } = () => { };

    public float K => Dpi.Of(Owner) / 96f;
    public int S(int v) => (int)Math.Ceiling(v * K);
    public float Sf(float v) => v * K;
    public bool Dark => Theme.Dark;
    public string LangCode => Lang.CurrentCode;
    public bool Writable => D.Writable();

    /// <summary>Point-sized font, cached for the life of the canvas (Dpi.Font creates a new one per call). Never dispose it.</summary>
    public Font Font(float em, FontStyle style = FontStyle.Regular)
    {
        if (!_fonts.TryGetValue((em, style), out var f)) _fonts[(em, style)] = f = Dpi.Font("Segoe UI", em, style);
        return f;
    }

    /// <summary>Pixel-sized font for text that scales with a drawn shape (the ring value), cached. Never dispose it.</summary>
    public Font PxFont(float px, FontStyle style = FontStyle.Regular)
    {
        int key = Math.Max(6, (int)Math.Round(px));
        if (!_pxFonts.TryGetValue((key, style), out var f)) _pxFonts[(key, style)] = f = Dpi.Font("Segoe UI", key, style, GraphicsUnit.Pixel);
        return f;
    }

    /// <summary>Opens another main tab (a ring opens Status, the curve opens the fan-curve editor).</summary>
    public void OpenTab(MainTab tab) => (Owner.FindForm() as MainForm)?.ShowTab(tab);
}
