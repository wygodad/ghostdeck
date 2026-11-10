namespace GhostDeck;

/// <summary>
/// The shape every Dashboard widget has, free and paid alike. A widget is not a control: it
/// paints into the rectangle the canvas gives it and answers clicks there. Two layers:
/// <see cref="PaintStatic"/> (dial, scale, title: rendered once into a bitmap and kept until
/// the size, the scale, the theme or the language change) and <see cref="PaintLive"/> (needle,
/// number, operating point: every tick, but only for widgets whose <see cref="LiveChanged"/>
/// says something moved). A widget never reads hardware itself and never writes to the EC
/// other than through the actions in <see cref="DashContext"/>, with the same gates as the
/// rest of the UI.
/// </summary>
internal interface IDashWidget
{
    /// <summary>Stable id stored in settings.json and in the statistics report, e.g. "core.cpuTemp".</summary>
    string TypeId { get; }

    /// <summary>Lang.T key of the name shown in the catalogue and as the widget title.</summary>
    string NameKey { get; }

    /// <summary>Allowed sizes in grid cells (Width = columns, Height = rows); the first is the default.</summary>
    IReadOnlyList<Size> Sizes { get; }

    void PaintStatic(Graphics g, Rectangle r, DashContext c);
    void PaintLive(Graphics g, Rectangle r, DashContext c);
    bool LiveChanged(DashContext c);
    void Click(Point p, Rectangle r, DashContext c);

    /// <summary>
    /// What the user can set on this widget (1.45), in the order the settings card shows it;
    /// empty = nothing to set, and the tile's menu offers no settings. The context is there
    /// because a list can depend on the machine (which switches this laptop has).
    /// </summary>
    IReadOnlyList<DashOptionDef> OptionDefs(DashContext c);
}

/// <summary>Common ground for the built-in widgets: the identity from the registry entry and the card frame.</summary>
internal abstract class DashWidget : IDashWidget
{
    protected DashWidget(string typeId, string nameKey, IReadOnlyList<Size> sizes, DashOptions? opt = null)
    {
        TypeId = typeId; NameKey = nameKey; Sizes = sizes; Opt = opt ?? new DashOptions();
    }

    protected DashWidget(DashWidgetDef def, DashOptions opt) : this(def.TypeId, def.NameKey, def.Sizes, opt) { }

    /// <summary>This instance's settings, from its layout item; fixed for the life of the instance.</summary>
    protected DashOptions Opt { get; }

    public string TypeId { get; }
    public string NameKey { get; }
    public IReadOnlyList<Size> Sizes { get; }

    public abstract void PaintStatic(Graphics g, Rectangle r, DashContext c);
    public virtual void PaintLive(Graphics g, Rectangle r, DashContext c) { }
    public virtual bool LiveChanged(DashContext c) => false;
    public virtual void Click(Point p, Rectangle r, DashContext c) { }
    public virtual IReadOnlyList<DashOptionDef> OptionDefs(DashContext c) => Array.Empty<DashOptionDef>();

    /// <summary>The card every widget sits on (same look as the cards on the other tabs).</summary>
    protected static void Frame(Graphics g, Rectangle r) => Ui.FillCard(g, r);

    /// <summary>Inner area of the card, with the standard padding.</summary>
    protected static Rectangle Inner(Rectangle r, DashContext c)
    {
        int p = c.S(16);
        return Rectangle.Inflate(r, -p, -p);
    }
}
