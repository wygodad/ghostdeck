using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>A widget type the application knows: identity, allowed sizes, availability on this model, and how to make one.</summary>
internal sealed record DashWidgetDef(string TypeId, string NameKey, Size[] Sizes, Func<MainDeps, bool> Available, Func<DashWidgetDef, DashOptions, IDashWidget> Create)
{
    /// <summary>When set and false, the catalogue does not list the type at all (it still paints when already placed).</summary>
    public Func<MainDeps, bool>? Listed { get; init; }

    /// <summary>A new instance with the given settings (none = the defaults). Settings are fixed for the life of the instance: changed settings mean a new instance.</summary>
    public IDashWidget New(DashOptions? opt = null) => Create(this, opt ?? new DashOptions());
}

/// <summary>
/// The catalogue of built-in widgets, by type id. The ids are stored in settings.json and sent
/// in the statistics report, so they never change once shipped. A type that is not here (a
/// paid add-on without its licence, a newer version's widget) paints as <see cref="UnknownWidget"/>
/// and stays in the layout.
/// </summary>
internal static class DashRegistry
{
    private static Size[] Sz(params (int w, int h)[] s) => s.Select(x => new Size(x.w, x.h)).ToArray();
    private static bool Always(MainDeps d) => true;

    public static readonly IReadOnlyList<DashWidgetDef> All = new[]
    {
        new DashWidgetDef("core.cpuTemp",     "dash_w_cpu_temp",  Sz((1, 1), (2, 2)), Always, (d, o) => new RingWidget(d, o, RingWidget.Kind.CpuTemp)),
        new DashWidgetDef("core.gpuTemp",     "dash_w_gpu_temp",  Sz((1, 1), (2, 2)), Always, (d, o) => new RingWidget(d, o, RingWidget.Kind.GpuTemp)),
        new DashWidgetDef("core.cpuFan",      "dash_w_cpu_fan",   Sz((1, 1), (2, 2)), Always, (d, o) => new RingWidget(d, o, RingWidget.Kind.CpuFan)),
        new DashWidgetDef("core.gpuFan",      "dash_w_gpu_fan",   Sz((1, 1), (2, 2)), Always, (d, o) => new RingWidget(d, o, RingWidget.Kind.GpuFan)),
        new DashWidgetDef("core.cpuUsage",    "dash_w_cpu_usage", Sz((1, 1), (2, 2)), Always, (d, o) => new RingWidget(d, o, RingWidget.Kind.CpuUsage)),
        new DashWidgetDef("core.cpuClock",    "dash_w_cpu_clock", Sz((1, 1), (2, 1)), Always, (d, o) => new CpuClockWidget(d, o)),
        new DashWidgetDef("core.profiles",    "dash_w_profiles",  Sz((4, 1), (2, 2)), Always, (d, o) => new ProfilesWidget(d, o)),
        new DashWidgetDef("core.curve",       "dash_w_curve",     Sz((2, 2), (4, 2)), d => d.HasFanCurve(), (d, o) => new CurveWidget(d, o)),
        // (1.45) 1x1: one button that runs the first of the widget's scenes
        new DashWidgetDef("core.scenes",      "dash_w_scenes",    Sz((2, 1), (2, 2), (1, 1)), Always, (d, o) => new ScenesWidget(d, o)),
        new DashWidgetDef("core.toggles",     "dash_w_toggles",   Sz((2, 1), (4, 1)), Always, (d, o) => new TogglesWidget(d, o)),
        new DashWidgetDef("core.memory",      "dash_w_memory",    Sz((2, 1), (2, 2)), Always, (d, o) => new MemoryWidget(d, o)),
        new DashWidgetDef("core.chargeLimit", "dash_w_charge",    Sz((2, 1)),         Always, (d, o) => new ChargeLimitWidget(d, o)),
        // (1.44) the community numbers; listed only while statistics are on (the view is the return for sharing)
        new DashWidgetDef("core.community",   "dash_w_community", Sz((4, 1), (4, 2)), Always, (d, o) => new CommunityWidget(d, o)) { Listed = d => d.Settings.StatsEnabled },
    };

    public static DashWidgetDef? Find(string typeId)
    {
        foreach (var d in All) if (d.TypeId == typeId) return d;
        return null;
    }

    /// <summary>
    /// The size an item takes once it stands on the Dashboard: a known widget takes the nearest
    /// size it allows for its settings (<see cref="DashItem.Fit"/>), so a 1x1 written for a widget
    /// whose smallest size is 2x2 is four cells, not one. A type this version does not know keeps
    /// the size it came with.
    /// </summary>
    public static (int w, int h) FittedSize(DashItem item, DashOptions? with = null) =>
        Find(item.Type) is { } def ? DashItem.Fit(def.New(with ?? DashOptions.Of(item)).Sizes, item.W, item.H) : (item.W, item.H);

    /// <summary>The cells a layout covers on the Dashboard, by the sizes its widgets really take.</summary>
    public static int FittedCells(IEnumerable<DashItem> items)
    {
        int cells = 0;
        foreach (var it in items)
        {
            var (w, h) = FittedSize(it);
            cells += Math.Clamp(w, 1, DashItem.MaxW) * Math.Clamp(h, 1, DashItem.MaxH);
        }
        return cells;
    }

    /// <summary>The widget for a layout item: the registered type, or the grey unknown card.</summary>
    public static IDashWidget Make(DashItem item) => Find(item.Type)?.New(DashOptions.Of(item)) ?? new UnknownWidget(item.Type);
}

/// <summary>A layout entry whose type this version does not know: a grey field with the id, kept in place so nothing is lost.</summary>
internal sealed class UnknownWidget : DashWidget
{
    private static readonly Size[] Any = { new(1, 1), new(2, 1), new(2, 2), new(4, 1), new(4, 2), new(6, 1) };

    public UnknownWidget(string typeId) : base(typeId, "dash_w_unknown", Any) { }

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.RoundRect(r, c.S(12)))
        {
            using var b = new SolidBrush(Theme.Card);
            g.FillPath(b, path);
            using var pen = new Pen(Theme.Faint) { DashStyle = DashStyle.Dash };
            g.DrawPath(pen, path);
        }
        var inner = Inner(r, c);
        var nf = c.Font(10.5f, FontStyle.Bold);
        var idf = c.Font(9f);
        int nameH = TextRenderer.MeasureText(Lang.T(NameKey), nf, new Size(inner.Width, int.MaxValue), TextFormatFlags.WordBreak).Height;
        int idH = idf.Height;
        int top = inner.Top + Math.Max(0, (inner.Height - nameH - idH - c.S(4)) / 2);
        TextRenderer.DrawText(g, Lang.T(NameKey), nf, new Rectangle(inner.Left, top, inner.Width, nameH), Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, TypeId, idf, new Rectangle(inner.Left, top + nameH + c.S(4), inner.Width, idH), Theme.Faint,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
    }
}
