namespace GhostDeck;

/// <summary>
/// CPU clock: the current frequency as a large number, the name under it.
/// Settings (1.45): the unit (MHz as in 1.44, or GHz), the colour of the number, the label and
/// its colour.
/// </summary>
internal sealed class CpuClockWidget : DashWidget
{
    private const string KUnit = "unit", KColor = "color";
    private static readonly string[] Units = { "mhz", "ghz" };

    private readonly bool _ghz;
    private int _painted = int.MinValue;

    public CpuClockWidget(DashWidgetDef def, DashOptions opt) : base(def, opt)
    {
        _ghz = opt.Choice(KUnit, "mhz", Units) == "ghz";
    }

    private (Rectangle value, Rectangle unit, Rectangle label) Layout(Rectangle r, DashContext c)
    {
        var inner = Inner(r, c);
        var lf = c.Font(10f);
        int labelH = Math.Min(lf.Height * 2, TextRenderer.MeasureText(Caption, lf, new Size(inner.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height);
        var vf = ValueFont(inner, c); var uf = UnitFont(inner, c);
        int gap = c.S(4), gap2 = c.S(10);
        int total = vf.Height + gap + uf.Height + gap2 + labelH;
        int top = inner.Top + Math.Max(0, (inner.Height - total) / 2);
        return (new Rectangle(inner.Left, top, inner.Width, vf.Height),
                new Rectangle(inner.Left, top + vf.Height + gap, inner.Width, uf.Height),
                new Rectangle(inner.Left, top + vf.Height + gap + uf.Height + gap2, inner.Width, labelH));
    }

    private static Font ValueFont(Rectangle inner, DashContext c) => c.PxFont(Math.Min(inner.Height * 0.30f, inner.Width * 0.22f), FontStyle.Bold);
    private static Font UnitFont(Rectangle inner, DashContext c) => c.PxFont(Math.Min(inner.Height * 0.14f, inner.Width * 0.10f));

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c)
    {
        Frame(g, r);
        var (_, _, label) = Layout(r, c);
        TextRenderer.DrawText(g, Caption, c.Font(10f), label, Tint("labelc", Theme.Muted),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        var inner = Inner(r, c);
        var (value, unit, _) = Layout(r, c);
        int mhz = c.Sample.CpuClockMhz;
        string text = mhz <= 0 ? "-" : _ghz ? (mhz / 1000.0).ToString("0.00", Lang.Culture) : mhz.ToString();
        TextRenderer.DrawText(g, text, ValueFont(inner, c), value, Tint(KColor, Theme.Text),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, _ghz ? "GHz" : "MHz", UnitFont(inner, c), unit, Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        _painted = mhz;
    }

    public override bool LiveChanged(DashContext c) => c.Sample.CpuClockMhz != _painted;
    public override void Click(Point p, Rectangle r, DashContext c) => c.OpenTab(MainTab.Status);

    public override IReadOnlyList<DashOptionDef> OptionDefs(DashContext c) => new[]
    {
        new DashOptionDef(KColor, DashOptKind.Color, Lang.T("dash_opt_value_color")) { Default = "auto", Auto = _ => Theme.Text },
        new DashOptionDef(KUnit, DashOptKind.Choice, Lang.T("dash_opt_unit")) { Default = "mhz", Items = new[] { ("mhz", "MHz"), ("ghz", "GHz") } },
        LabelDef(Theme.Muted),
    };
}
