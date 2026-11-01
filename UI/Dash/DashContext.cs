namespace GhostDeck;

/// <summary>
/// What a widget gets from the canvas, and nothing more: the shared data sample of the current
/// tick, the actions of <see cref="MainDeps"/> (profile, scene, Fan Boost, switches, charge
/// limit), the scale, the theme and the language. One instance per canvas; the sample is
/// replaced on every tick.
/// </summary>
internal sealed class DashContext
{
    public DashContext(MainDeps d, Control owner) { D = d; Owner = owner; }

    /// <summary>Data and actions of the application. Writes go through its actions only.</summary>
    public MainDeps D { get; }

    /// <summary>The canvas; the scale is read from it (Dpi.Of), never from a cached value.</summary>
    public Control Owner { get; }

    /// <summary>The sample of the current tick (empty until the first read).</summary>
    public DashSample Sample { get; internal set; } = DashSample.Empty;

    public float K => Dpi.Of(Owner) / 96f;
    public int S(int v) => (int)Math.Ceiling(v * K);
    public Font Font(float em, FontStyle style = FontStyle.Regular) => Dpi.Font("Segoe UI", em, style);
    public bool Dark => Theme.Dark;
    public string LangCode => Lang.CurrentCode;
}
