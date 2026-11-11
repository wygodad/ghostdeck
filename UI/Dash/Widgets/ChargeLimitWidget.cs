namespace GhostDeck;

/// <summary>
/// Charge limit: the segments of the Scenarios control (Off / 60 % / 80 % / 100 %, plus the
/// custom value when one is set) drawn on the card; a click sets the limit through
/// <see cref="MainDeps.SetChargeLimit"/>, which also ends travel mode as the Scenarios control does.
///
/// Settings (1.45): three ways to show it. "Segments" is the look of 1.44. "Battery" adds a bar
/// with the charge the battery holds now (from Windows, no hardware read) and a mark where the
/// limit is. "Small" fits a single cell: the limit as a large number, and a click steps to the
/// next value. Also the colour of the picked segment, of the bar and of the mark, the title and
/// its colour.
/// </summary>
internal sealed class ChargeLimitWidget : DashWidget
{
    private const string KView = "view", KColor = "color", KBar = "barc", KMark = "limc";
    private static readonly string[] Views = { "seg", "bat", "small" };

    /// <summary>Preview tool only: a battery level to draw when the machine it runs on has no battery to ask.</summary>
    internal static int? PreviewBattery { get; set; }

    private readonly string _view;
    private string _painted = "";
    private readonly List<(Rectangle r, int value)> _zones = new();

    public ChargeLimitWidget(DashWidgetDef def, DashOptions opt) : base(def, opt)
    {
        _view = opt.Choice(KView, "seg", Views);
        if (_view == "small") Sizes = Sz((1, 1));
    }

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

    private static int Battery(DashSample s) => PreviewBattery ?? s.BatteryPercent;

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c)
    {
        Frame(g, r);
        if (_view != "small") DashPaint.Title(g, Inner(r, c), c, Caption, Tint("labelc", Theme.Text));
    }

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        _zones.Clear();
        _painted = Signature(c.Sample);
        var segs = Segments(c.Sample);
        int sel = SegmentIndex(c.Sample, segs);
        if (_view == "small") { PaintSmall(g, r, c, segs, sel); return; }

        var inner = Inner(r, c);
        var f = c.Font(10f, FontStyle.Bold);
        int titleH = c.Font(11f, FontStyle.Bold).Height;
        int segH = Math.Max(c.S(34), f.Height + c.S(14));
        int top;
        if (_view == "bat")
        {
            // The charge now right after the title, the bar with the limit's mark, the segments under
            // it. After the title, not at the card's right edge: that corner is where the tile's menu
            // button comes up on hover, and it would cover the number.
            int pct = Battery(c.Sample), barH = c.S(12), gap = Math.Max(c.S(8), (inner.Height - titleH - barH - segH) / 3);
            var vf = c.Font(9.5f);
            string now = pct >= 0 ? pct + "%" : "-";
            int titleW = TextRenderer.MeasureText(Caption, c.Font(11f, FontStyle.Bold), Size.Empty, TextFormatFlags.NoPadding).Width;
            int nowW = TextRenderer.MeasureText(now, vf, Size.Empty, TextFormatFlags.NoPadding).Width;
            int nowX = inner.Left + titleW + c.S(10);
            if (nowX + nowW <= inner.Right - c.S(22))   // a title so long that the number would reach the corner: the bar says it anyway
                TextRenderer.DrawText(g, now, vf, new Rectangle(nowX, inner.Top, nowW + c.S(2), titleH), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var bar = new RectangleF(inner.Left, inner.Top + titleH + gap, inner.Width, barH);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            DashPaint.Bar(g, bar, Math.Max(0, pct) / 100f, Tint(KBar, Theme.Green));
            int limit = c.Sample.ChargeLimit;
            if (limit is > 0 and <= 100)
            {
                float mx = bar.Left + bar.Width * limit / 100f;
                using var pen = new Pen(Tint(KMark, Theme.Amber), c.Sf(2f));
                g.DrawLine(pen, mx, bar.Top - c.Sf(4), mx, bar.Bottom + c.Sf(4));
            }
            top = (int)bar.Bottom + gap;
        }
        else top = inner.Top + titleH + Math.Max(c.S(8), (inner.Height - titleH - segH) / 2);

        var track = new Rectangle(inner.Left, top, inner.Width, segH);
        using (var path = Theme.RoundRect(track, c.Sf(8)))
        {
            using var b = new SolidBrush(Theme.Surface);
            g.FillPath(b, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
        int pad = c.S(3), segW = (track.Width - pad * 2) / segs.Length;
        for (int i = 0; i < segs.Length; i++)
        {
            var sr = new Rectangle(track.Left + pad + i * segW, track.Top + pad, segW, segH - pad * 2);
            if (i == sel)
            {
                using var path = Theme.RoundRect(sr, c.Sf(6));
                using var b = new SolidBrush(Tint(KColor, Theme.AccentFill));
                g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, segs[i].label, f, sr, i == sel ? Color.White : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            _zones.Add((sr, segs[i].value));
        }
    }

    /// <summary>The single-cell look: the limit as a large number, the name, a dot per value; the whole card steps to the next value.</summary>
    private void PaintSmall(Graphics g, Rectangle r, DashContext c, (string label, int value)[] segs, int sel)
    {
        var inner = Inner(r, c);
        var vf = c.PxFont(Math.Min(inner.Height * 0.30f, inner.Width * 0.24f), FontStyle.Bold);
        var lf = c.Font(10f);
        int labelH = Math.Min(lf.Height * 2, TextRenderer.MeasureText(Caption, lf, new Size(inner.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height);
        int dot = c.S(7), g1 = c.S(6), g2 = c.S(10);
        int top = inner.Top + Math.Max(0, (inner.Height - (vf.Height + g1 + labelH + g2 + dot)) / 2);
        TextRenderer.DrawText(g, segs[sel].label, vf, new Rectangle(inner.Left, top, inner.Width, vf.Height), Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Caption, lf, new Rectangle(inner.Left, top + vf.Height + g1, inner.Width, labelH), Tint("labelc", Theme.Muted),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        int step = dot + c.S(7), x0 = inner.Left + (inner.Width - (segs.Length * step - c.S(7))) / 2, dy = top + vf.Height + g1 + labelH + g2;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        for (int i = 0; i < segs.Length; i++)
        {
            using var b = new SolidBrush(i == sel ? Tint(KColor, Theme.AccentFill) : Theme.BorderStrong);
            g.FillEllipse(b, x0 + i * step, dy, dot, dot);
        }
        _zones.Add((r, segs[(sel + 1) % segs.Length].value));
    }

    private string Signature(DashSample s) => _view == "bat" ? s.ChargeLimit + "|" + Battery(s) : s.ChargeLimit.ToString();

    public override bool LiveChanged(DashContext c) => Signature(c.Sample) != _painted;

    public override void Click(Point p, Rectangle r, DashContext c)
    {
        foreach (var (zr, value) in _zones)
            if (zr.Contains(p)) { c.D.SetChargeLimit(value); return; }
    }

    public override IReadOnlyList<DashOptionDef> OptionDefs(DashContext c)
    {
        static bool Bat(DashOptions o) => o.Choice(KView, "seg", Views) == "bat";
        return new[]
        {
            new DashOptionDef(KView, DashOptKind.Choice, Lang.T("dash_opt_view"))
            {
                Default = "seg",
                Items = new[] { ("seg", Lang.T("dash_opt_view_seg")), ("bat", Lang.T("dash_opt_view_bat")), ("small", Lang.T("dash_opt_view_small")) },
            },
            new DashOptionDef(KColor, DashOptKind.Color, Lang.T("dash_opt_sel_color")) { Default = "auto", Auto = _ => Theme.AccentFill },
            LabelDef(Theme.Text),
            // the two colours of the battery look; dimmed in the other looks
            new DashOptionDef(KBar, DashOptKind.Tint, Lang.T("dash_opt_bar_color")) { Auto = _ => Theme.Green, EnabledWhen = Bat, Row = "bat" },
            new DashOptionDef(KMark, DashOptKind.Tint, Lang.T("dash_opt_limit_color")) { Auto = _ => Theme.Amber, EnabledWhen = Bat, Row = "bat" },
        };
    }
}
