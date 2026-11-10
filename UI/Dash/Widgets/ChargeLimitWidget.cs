namespace GhostDeck;

/// <summary>
/// Charge limit: the segments of the Scenarios control (Off / 60 % / 80 % / 100 %, plus the
/// custom value when one is set) drawn on the card; a click sets the limit through
/// <see cref="MainDeps.SetChargeLimit"/>, which also ends travel mode as the Scenarios control does.
/// </summary>
internal sealed class ChargeLimitWidget : DashWidget
{
    private int _painted = int.MinValue;
    private readonly List<(Rectangle r, int value)> _zones = new();

    public ChargeLimitWidget(DashWidgetDef def, DashOptions opt) : base(def, opt) { }

    private static (string label, int value)[] Segments(DashSample s)
    {
        var list = new List<(string, int)> { (Lang.T("gen_off_short"), 0), ("60%", 60), ("80%", 80), ("100%", 100) };
        if (AppSettings.ChargeManaged(s.ChargeLimit) && !AppSettings.ChargeVerified(s.ChargeLimit)) list.Add((s.ChargeLimit + "%", s.ChargeLimit));
        return list.ToArray();
    }

    private static int SegmentIndex(DashSample s, (string label, int value)[] segs)
    {
        for (int i = 0; i < segs.Length; i++) if (segs[i].value == s.ChargeLimit) return i;
        return 0;
    }

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c)
    {
        Frame(g, r);
        DashPaint.Title(g, Inner(r, c), c, Lang.T(NameKey));
    }

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        var inner = Inner(r, c);
        var f = c.Font(10f, FontStyle.Bold);
        int titleH = c.Font(11f, FontStyle.Bold).Height;
        int segH = Math.Max(c.S(34), f.Height + c.S(14));
        int top = inner.Top + titleH + Math.Max(c.S(8), (inner.Height - titleH - segH) / 2);
        var segs = Segments(c.Sample);
        int sel = SegmentIndex(c.Sample, segs);
        var track = new Rectangle(inner.Left, top, inner.Width, segH);
        using (var path = Theme.RoundRect(track, c.Sf(8)))
        {
            using var b = new SolidBrush(Theme.Surface);
            g.FillPath(b, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
        _zones.Clear();
        int pad = c.S(3), segW = (track.Width - pad * 2) / segs.Length;
        for (int i = 0; i < segs.Length; i++)
        {
            var sr = new Rectangle(track.Left + pad + i * segW, track.Top + pad, segW, segH - pad * 2);
            if (i == sel)
            {
                using var path = Theme.RoundRect(sr, c.Sf(6));
                using var b = new SolidBrush(Theme.AccentFill);
                g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, segs[i].label, f, sr, i == sel ? Color.White : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            _zones.Add((sr, segs[i].value));
        }
        _painted = c.Sample.ChargeLimit;
    }

    public override bool LiveChanged(DashContext c) => c.Sample.ChargeLimit != _painted;

    public override void Click(Point p, Rectangle r, DashContext c)
    {
        foreach (var (zr, value) in _zones)
            if (zr.Contains(p)) { c.D.SetChargeLimit(value); return; }
    }
}
