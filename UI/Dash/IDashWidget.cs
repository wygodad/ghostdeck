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

    /// <summary>
    /// Allowed sizes in grid cells (Width = columns, Height = rows); the first is the default.
    /// They may depend on the widget's settings (1.45): Profiles showing one profile fits a single
    /// cell, with four it needs four.
    /// </summary>
    IReadOnlyList<Size> Sizes { get; }

    void PaintStatic(Graphics g, Rectangle r, DashContext c);
    void PaintLive(Graphics g, Rectangle r, DashContext c);
    bool LiveChanged(DashContext c);
    void Click(Point p, Rectangle r, DashContext c);

    /// <summary>
    /// Whether the widget shows something at the cursor while it is over it (a chart: the reading
    /// at that moment). The canvas then repaints the widget as the cursor moves in it and calls
    /// <see cref="PaintPointer"/> after the live layer. Never in edit mode.
    /// </summary>
    bool FollowsPointer { get; }

    /// <summary>What the widget shows at <paramref name="p"/> (canvas coordinates, inside <paramref name="r"/>); nothing when the point is not over what it tells about.</summary>
    void PaintPointer(Graphics g, Rectangle r, Point p, DashContext c);

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
    /// <summary>The registry's sizes, unless the widget's constructor sets its own for its settings.</summary>
    public IReadOnlyList<Size> Sizes { get; protected init; }

    protected static Size[] Sz(params (int w, int h)[] s) => s.Select(x => new Size(x.w, x.h)).ToArray();

    /// <summary>The colour of a colour setting: "auto" (<paramref name="own"/>) or one of the palette.</summary>
    protected Color Tint(string key, Color own) => DashPaint.Pick(Opt.Choice(key, "auto", DashPaint.PaletteChoices), own);

    /// <summary>The widget's title: the label the user gave it, or its name in the app's language.</summary>
    protected string Caption => Opt.Text("label") is { Length: > 0 } t ? t.Replace("&", "&&") : Lang.T(NameKey);   // "&&": a label is text, the drawing would take a lone "&" for a shortcut mark

    /// <summary>The "Label" field with its colour button, the same in every widget that has a title.</summary>
    protected DashOptionDef LabelDef(Color own) =>
        new("label", DashOptKind.Text, Lang.T("dash_opt_label")) { Placeholder = Lang.T(NameKey), ColorKey = "labelc", Auto = _ => own };

    /// <summary>
    /// What a "which to show" setting leaves of <paramref name="all"/>, in the order of
    /// <paramref name="all"/>: everything when nothing is stored, else the stored ids that still
    /// exist (a deleted scene, an unplugged disk simply drop out).
    /// </summary>
    protected IReadOnlyList<T> Picked<T>(string key, IReadOnlyList<T> all, Func<T, string> id)
    {
        var sel = Opt.List(key);
        if (sel == null) return all;
        return all.Where(x => sel.Contains(DashOptions.Clean(id(x), DashOptions.MaxText))).ToList();
    }

    public abstract void PaintStatic(Graphics g, Rectangle r, DashContext c);
    public virtual void PaintLive(Graphics g, Rectangle r, DashContext c) { }
    public virtual bool LiveChanged(DashContext c) => false;
    public virtual void Click(Point p, Rectangle r, DashContext c) { }
    public virtual bool FollowsPointer => false;
    public virtual void PaintPointer(Graphics g, Rectangle r, Point p, DashContext c) { }
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
