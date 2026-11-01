namespace GhostDeck;

/// <summary>CPU clock: the current frequency in MHz as a large number, the name under it.</summary>
internal sealed class CpuClockWidget : DashWidget
{
    private int _painted = int.MinValue;

    public CpuClockWidget(DashWidgetDef def) : base(def.TypeId, def.NameKey, def.Sizes) { }

    private (Rectangle value, Rectangle unit, Rectangle label) Layout(Rectangle r, DashContext c)
    {
        var inner = Inner(r, c);
        var lf = c.Font(10f);
        int labelH = Math.Min(lf.Height * 2, TextRenderer.MeasureText(Lang.T(NameKey), lf, new Size(inner.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height);
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
        TextRenderer.DrawText(g, Lang.T(NameKey), c.Font(10f), label, Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        var inner = Inner(r, c);
        var (value, unit, _) = Layout(r, c);
        int mhz = c.Sample.CpuClockMhz;
        TextRenderer.DrawText(g, mhz > 0 ? mhz.ToString() : "-", ValueFont(inner, c), value, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "MHz", UnitFont(inner, c), unit, Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        _painted = mhz;
    }

    public override bool LiveChanged(DashContext c) => c.Sample.CpuClockMhz != _painted;
    public override void Click(Point p, Rectangle r, DashContext c) => c.OpenTab(MainTab.Status);
}
