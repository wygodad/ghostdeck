using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// CPU power (1.46, roadmap #116): the processor package's power draw in watts, from Windows'
/// Energy Meter counter of the package domain (Perf.CpuWatts), the processor's own energy
/// accounting read with no driver; the widget is offered only on processors that expose it.
///
/// Three looks (the "look" setting):
/// - Number: the value as a large number with "W" under it and the name below, the CPU clock
///   widget's layout.
/// - Dial: the Status ring, segmented or continuous, lit to the value's share of a scale; the
///   value and "W" inside, the name below. A dial says at a glance how much of the budget the
///   processor is using.
/// - Trail: the history widgets' chart of the last two minutes - the title with the value of now
///   beside it, a scale in watts on the left with grid lines, the time under the plot, the trail
///   as a filled line, and the reading at the cursor in a bubble. In a cell too small for a
///   scale (1x1) the trail is the value and the line alone. A trail shows what a profile switch
///   or a load does over time, which a number cannot.
/// The scale (dial and trail) is 45 / 65 / 100 / 150 / 200 / 250 W, or Auto: the highest value
/// seen in the last two minutes rounded up to the next 25 W, so an idle laptop is not a sliver
/// on a 250 W dial. Settings as on the other widgets: the colour, the label and its colour.
/// </summary>
internal sealed class CpuPowerWidget : DashWidget
{
    private const string KColor = "color", KLook = "look", KDial = "dial", KScale = "scale", KRange = "range";
    internal static readonly string[] Looks = { "number", "dial", "trail" };
    internal static readonly string[] Scales = { "auto", "45", "65", "100", "150", "200", "250" };
    internal static readonly string[] Ranges = { "2", "5", "15", "30" };   // minutes in view (the trail look)
    private const TextFormatFlags Plain = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
    private const TextFormatFlags Wrapped = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

    private readonly string _look, _scale;
    private readonly bool _arc;
    private readonly int _minutes;
    private TimeSpan Span => TimeSpan.FromMinutes(_minutes);
    private string _painted = "";
    // what the last trail paint drew, for the reading at the cursor: the same points, scale and place
    private Rectangle _plot, _plotOf;
    private IReadOnlyList<(DateTime At, double W)> _drawn = Array.Empty<(DateTime, double)>();
    private DateTime _drawnStart;
    private int _drawnTop;

    public CpuPowerWidget(DashWidgetDef def, DashOptions opt) : base(def, opt)
    {
        _look = opt.Choice(KLook, "number", Looks);
        _arc = opt.Choice(KDial, "seg", RingWidget.Dials) == "arc";
        _scale = opt.Choice(KScale, "auto", Scales);
        _minutes = int.Parse(opt.Choice(KRange, "2", Ranges), Lang.Culture);
    }

    // whole watts: the counter moves every tick, and tenths would only flicker
    private static int Watts(DashContext c) => c.Sample.CpuWatts < 0 ? -1 : (int)Math.Round(c.Sample.CpuWatts);

    /// <summary>The top of the dial and the trail: the picked scale, or the two-minute peak rounded up to 25 W.</summary>
    private int Ceiling(DashContext c)
    {
        if (_scale != "auto") return int.Parse(_scale, Lang.Culture);
        double peak = Math.Max(c.Sample.CpuWatts, 0);
        var cut = DateTime.UtcNow - Span;   // the peak of the window in view (the dial: the last two minutes)
        foreach (var (at, w) in Perf.CpuWattsTrail()) if (at >= cut) peak = Math.Max(peak, w);
        return Math.Max(25, (int)Math.Ceiling(peak / 25.0) * 25);
    }

    /// <summary>How many grid steps divide a scale into whole watts: 4, 5, 3 or 2, the first that fits.</summary>
    private static int Steps(int top)
    {
        foreach (int n in new[] { 4, 5, 3, 2 }) if (top % n == 0) return n;
        return 2;
    }

    private Color ValueColor => Tint(KColor, _look == "number" ? Theme.Text : Theme.Accent);

    // ---- number: the CPU clock widget's layout ----
    private (Rectangle value, Rectangle unit, Rectangle label) NumberLayout(Rectangle r, DashContext c)
    {
        var inner = Inner(r, c);
        var lf = c.Font(10f);
        int labelH = Math.Min(lf.Height * 2, TextRenderer.MeasureText(Caption, lf, new Size(inner.Width, int.MaxValue), Wrapped).Height);
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

    // ---- dial: the ring widget's layout ----
    private (RectangleF box, Rectangle label) DialLayout(Rectangle r, DashContext c)
    {
        var inner = Inner(r, c);
        var lf = c.Font(10f);
        int labelH = Math.Min(lf.Height * 2, TextRenderer.MeasureText(Caption, lf, new Size(inner.Width, int.MaxValue), Wrapped).Height);
        int gap = c.S(10);
        int d = Math.Max(c.S(40), Math.Min(inner.Width, inner.Height - gap - labelH));
        int total = d + gap + labelH;
        int top = inner.Top + Math.Max(0, (inner.Height - total) / 2);
        return (new RectangleF(inner.Left + (inner.Width - d) / 2f, top, d, d), new Rectangle(inner.Left, top + d + gap, inner.Width, labelH));
    }

    // ---- trail: the history widgets' anatomy ----
    /// <summary>A cell too small for a scale and time labels: the value and the line alone (1x1).</summary>
    private static bool Compact(Rectangle inner, DashContext c) => inner.Height < c.S(90) || inner.Width < c.S(160);

    /// <summary>The title as the history widgets draw it: one line, or two, clear of the menu corner.</summary>
    private (string text, Rectangle box, bool oneLine) TitleBox(Rectangle inner, DashContext c)
    {
        var f = c.Font(11f, FontStyle.Bold);
        int w = Math.Max(c.S(40), inner.Width - c.S(22));
        string text = ScenesWidget.Fit(Caption, f, w, 2);
        var size = TextRenderer.MeasureText(text, f, new Size(w, int.MaxValue), Wrapped);
        int h = Math.Max(f.Height, size.Height);
        return (text, new Rectangle(inner.Left, inner.Top, Math.Min(w, size.Width), h), !text.Contains('\n') && h < f.Height * 2);
    }

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c)
    {
        Frame(g, r);
        var labelColor = Tint("labelc", Theme.Muted);
        const TextFormatFlags lflags = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | Wrapped;
        switch (_look)
        {
            case "dial":
            {
                var (box, label) = DialLayout(r, c);
                if (_arc) DashPaint.RingTrackArc(g, box); else DashPaint.RingTrack(g, box);
                TextRenderer.DrawText(g, Caption, c.Font(10f), label, labelColor, lflags);
                break;
            }
            case "trail":
            {
                var inner = Inner(r, c);
                if (Compact(inner, c))
                {
                    // the name under the line, as the number look has it
                    var lf = c.Font(10f);
                    int labelH = Math.Min(lf.Height * 2, TextRenderer.MeasureText(Caption, lf, new Size(inner.Width, int.MaxValue), Wrapped).Height);
                    TextRenderer.DrawText(g, Caption, lf, new Rectangle(inner.Left, inner.Bottom - labelH, inner.Width, labelH), labelColor, lflags);
                }
                else
                {
                    var (text, box, _) = TitleBox(inner, c);
                    TextRenderer.DrawText(g, text, c.Font(11f, FontStyle.Bold), new Rectangle(box.Left, box.Top, Math.Max(c.S(40), inner.Width - c.S(22)), box.Height),
                        Tint("labelc", Theme.Text), TextFormatFlags.Left | TextFormatFlags.Top | Wrapped);
                }
                break;
            }
            default:
            {
                var (_, _, label) = NumberLayout(r, c);
                TextRenderer.DrawText(g, Caption, c.Font(10f), label, labelColor, lflags);
                break;
            }
        }
    }

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        int w = Watts(c);
        string text = w < 0 ? "-" : w.ToString(Lang.Culture);
        switch (_look)
        {
            case "dial":
            {
                var (box, _) = DialLayout(r, c);
                int top = Ceiling(c);
                if (w >= 0)
                {
                    float fraction = Math.Clamp(w / (float)top, 0, 1);
                    if (_arc) DashPaint.RingLitArc(g, box, fraction, ValueColor); else DashPaint.RingLit(g, box, fraction, ValueColor);
                }
                DashPaint.RingValue(g, box, text, "W", c);
                break;
            }
            case "trail":
                PaintTrailLook(g, r, c, w);
                break;
            default:
            {
                var inner = Inner(r, c);
                var (value, unit, _) = NumberLayout(r, c);
                TextRenderer.DrawText(g, text, ValueFont(inner, c), value, ValueColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "W", UnitFont(inner, c), unit, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
                break;
            }
        }
        _painted = Signature(c);
    }

    /// <summary>
    /// The trail look's live layer: the value of now beside the title (or under it), the scale on
    /// the left with grid lines, the time under the plot, and the line. The compact form (1x1)
    /// keeps the value and the line only.
    /// </summary>
    private void PaintTrailLook(Graphics g, Rectangle r, DashContext c, int w)
    {
        _plot = Rectangle.Empty;
        var inner = Inner(r, c);
        int top = Ceiling(c);
        var color = ValueColor;
        string nowText = w < 0 ? "- W" : $"{w} W";
        Rectangle plot;
        if (Compact(inner, c))
        {
            var vf = c.PxFont(Math.Min(inner.Height * 0.24f, inner.Width * 0.16f), FontStyle.Bold);
            var lf = c.Font(10f);
            int labelH = Math.Min(lf.Height * 2, TextRenderer.MeasureText(Caption, lf, new Size(inner.Width, int.MaxValue), Wrapped).Height);
            // the value clear of the menu corner
            TextRenderer.DrawText(g, nowText, vf, new Rectangle(inner.Left, inner.Top, Math.Max(c.S(30), inner.Width - c.S(22)), vf.Height), color,
                TextFormatFlags.Left | TextFormatFlags.Top | Plain);
            int gap = c.S(6);
            int plotTop = inner.Top + vf.Height + gap, plotBottom = inner.Bottom - labelH - gap;
            plot = new Rectangle(inner.Left, plotTop, inner.Width, Math.Max(c.S(12), plotBottom - plotTop));
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
        }
        else
        {
            var titleF = c.Font(11f, FontStyle.Bold);
            var valF = c.Font(9f, FontStyle.Bold);
            var axisF = c.Font(8.5f);
            // the value of now: right of the title when it fits before the menu corner, else under it;
            // the slot is as wide as the widest value, so it does not jump as the number changes
            int need = TextRenderer.MeasureText("999 W", valF, Size.Empty, Plain).Width;
            var (_, title, oneLine) = TitleBox(inner, c);
            bool beside = oneLine && title.Right + c.S(14) + need <= inner.Right - c.S(22);
            int y0 = title.Bottom + c.S(8);
            int vx = beside ? inner.Right - c.S(22) - need : inner.Left;
            int vy = beside ? inner.Top + (titleF.Height - valF.Height) / 2 : y0;
            TextRenderer.DrawText(g, nowText, valF, new Rectangle(vx, vy, need + c.S(2), valF.Height), color, TextFormatFlags.Left | TextFormatFlags.Top | Plain);
            if (!beside) y0 += valF.Height + c.S(6);

            // the plot: a column for the scale on the left, a line for the time under it
            int axisW = TextRenderer.MeasureText(top + " W", axisF, Size.Empty, Plain).Width + c.S(6);
            int plotTop = y0 + axisF.Height / 2, plotBottom = inner.Bottom - axisF.Height - c.S(4);
            plot = new Rectangle(inner.Left + axisW, plotTop, inner.Width - axisW - c.S(2), plotBottom - plotTop);
            if (plot.Height < c.S(24) || plot.Width < c.S(40)) return;
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            // a short plot (2x1) takes two steps when they land on whole watts, so the labels do not touch
            int steps = plot.Height < c.S(70) && top % 2 == 0 ? 2 : Steps(top);
            using (var grid = new Pen(Theme.Border))
                for (int i = 0; i <= steps; i++)
                {
                    int v = top * i / steps;
                    int y = plot.Bottom - (int)Math.Round(i / (float)steps * plot.Height);
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    TextRenderer.DrawText(g, v + " W", axisF, new Rectangle(inner.Left, y - axisF.Height / 2, axisW - c.S(6), axisF.Height), Theme.Faint,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | Plain);
                }
            string from = "-" + _minutes + " min", to = Lang.T("st_hist_now");
            int toW = TextRenderer.MeasureText(to, axisF, Size.Empty, Plain).Width;
            TextRenderer.DrawText(g, from, axisF, new Point(plot.Left, plot.Bottom + c.S(4)), Theme.Faint, Plain);
            TextRenderer.DrawText(g, to, axisF, new Point(plot.Right - toW, plot.Bottom + c.S(4)), Theme.Faint, Plain);
            g.SmoothingMode = oldMode;
        }
        _plotOf = r;
        PaintLine(g, plot, top, color, c);
    }

    /// <summary>The window in view: time left to right, 0 to the scale's top bottom to top, a filled line; one reading is a dot.</summary>
    private void PaintLine(Graphics g, Rectangle plot, int top, Color color, DashContext c)
    {
        var now = DateTime.UtcNow;
        var start = now - Span;
        var all = Perf.CpuWattsTrail();
        var data = new List<(DateTime At, double W)>(all.Count);
        foreach (var p in all) if (p.At >= start && p.At <= now) data.Add(p);
        _plot = plot; _drawn = data; _drawnStart = start; _drawnTop = top;
        if (data.Count == 0 || plot.Width < 4 || plot.Height < 4) return;
        double span = Span.TotalSeconds;
        var pts = new PointF[data.Count];
        for (int i = 0; i < data.Count; i++)
        {
            float x = plot.Left + (float)(Math.Clamp((data[i].At - start).TotalSeconds / span, 0, 1) * plot.Width);
            float y = plot.Bottom - Math.Clamp((float)(data[i].W / top), 0f, 1f) * plot.Height;
            pts[i] = new PointF(x, y);
        }
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            if (pts.Length == 1)
            {
                float d = c.Sf(4);
                using var b = new SolidBrush(color);
                g.FillEllipse(b, pts[0].X - d / 2, pts[0].Y - d / 2, d, d);
                return;
            }
            // the area under the line, faint, then the line
            var poly = new PointF[pts.Length + 2];
            poly[0] = new PointF(pts[0].X, plot.Bottom);
            pts.CopyTo(poly, 1);
            poly[^1] = new PointF(pts[^1].X, plot.Bottom);
            using (var fill = new SolidBrush(Color.FromArgb(56, color))) g.FillPolygon(fill, poly);
            using var pen = new Pen(color, c.Sf(1.8f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, pts);
        }
        finally { g.SmoothingMode = old; }
    }

    public override bool FollowsPointer => _look == "trail";

    /// <summary>
    /// The reading at the cursor, as on the history widgets: a dashed line at the point nearest to
    /// the cursor's moment, a dot on the line, and a bubble with the time and the watts. Only
    /// while the cursor is over the plot.
    /// </summary>
    public override void PaintPointer(Graphics g, Rectangle r, Point p, DashContext c)
    {
        if (_look != "trail" || _plot.IsEmpty || _plotOf != r || _drawn.Count == 0) return;
        if (p.X < _plot.Left || p.X > _plot.Right || p.Y < _plot.Top - c.S(4) || p.Y > _plot.Bottom + c.S(4)) return;
        double span = Span.TotalSeconds;
        var at = _drawnStart + TimeSpan.FromSeconds((p.X - _plot.Left) / (double)Math.Max(1, _plot.Width) * span);
        int best = 0;
        double bestGap = double.MaxValue;
        for (int i = 0; i < _drawn.Count; i++)
        {
            double gap = Math.Abs((_drawn[i].At - at).TotalSeconds);
            if (gap < bestGap) { bestGap = gap; best = i; }
        }
        var s = _drawn[best];
        float x = _plot.Left + (float)(Math.Clamp((s.At - _drawnStart).TotalSeconds / span, 0, 1) * _plot.Width);
        float y = _plot.Bottom - Math.Clamp((float)(s.W / _drawnTop), 0f, 1f) * _plot.Height;
        var color = ValueColor;
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(Color.FromArgb(150, Theme.Muted), 1f) { DashStyle = DashStyle.Dash })
            g.DrawLine(pen, x, _plot.Top, x, _plot.Bottom);
        using (var ring = new SolidBrush(Theme.Card)) g.FillEllipse(ring, x - c.Sf(5), y - c.Sf(5), c.Sf(10), c.Sf(10));
        using (var dot = new SolidBrush(color)) g.FillEllipse(dot, x - c.Sf(3.5f), y - c.Sf(3.5f), c.Sf(7), c.Sf(7));

        // the bubble: the time, then the watts
        var timeF = c.Font(8.5f, FontStyle.Bold);
        var valF = c.Font(9f, FontStyle.Bold);
        var rows = new (string text, Font f, Color color)[]
        {
            (s.At.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), timeF, Theme.Muted),
            ($"{Math.Round(s.W)} W", valF, color),
        };
        int padX = c.S(9), padY = c.S(7), rowGap = c.S(2), bw = 0, bh = padY * 2 - rowGap;
        foreach (var (text, f, _) in rows)
        {
            bw = Math.Max(bw, TextRenderer.MeasureText(text, f, Size.Empty, Plain).Width);
            bh += f.Height + rowGap;
        }
        bw += padX * 2;
        // right of the line, left of it when there is no room; kept inside the widget
        int bx = (int)x + c.S(12);
        if (bx + bw > r.Right - c.S(6)) bx = (int)x - c.S(12) - bw;
        bx = Math.Max(r.Left + c.S(6), bx);
        int by = Math.Clamp(p.Y - bh / 2, r.Top + c.S(6), Math.Max(r.Top + c.S(6), r.Bottom - c.S(6) - bh));
        var box = new Rectangle(bx, by, bw, bh);
        using (var path = Theme.RoundRect(box, c.Sf(7)))
        {
            using var fill = new SolidBrush(Color.FromArgb(Theme.Dark ? 235 : 245, Theme.Card));
            g.FillPath(fill, path);
            using var edge = new Pen(Theme.Border);
            g.DrawPath(edge, path);
        }
        g.SmoothingMode = old;
        int ty = box.Top + padY;
        foreach (var (text, f, color2) in rows)
        {
            TextRenderer.DrawText(g, text, f, new Point(box.Left + padX, ty), color2, Plain);
            ty += f.Height + rowGap;
        }
    }

    private string Signature(DashContext c)
    {
        int w = Watts(c);
        if (_look == "number") return $"{w}";
        var trail = _look == "trail" ? Perf.CpuWattsTrail() : Array.Empty<(DateTime, double)>();
        return $"{w}|{Ceiling(c)}|{trail.Count}|{(trail.Count > 0 ? trail[^1].Item1.Ticks : 0)}";
    }

    public override bool LiveChanged(DashContext c) => Signature(c) != _painted;
    public override void Click(Point p, Rectangle r, DashContext c) => c.OpenTab(MainTab.Status);

    public override IReadOnlyList<DashOptionDef> OptionDefs(DashContext c) => new DashOptionDef[]
    {
        new(KLook, DashOptKind.Choice, Lang.T("dash_opt_view"))
        {
            Default = "number",
            Items = new[] { ("number", Lang.T("dash_pw_number")), ("dial", Lang.T("dash_pw_dial")), ("trail", Lang.T("dash_pw_trail")) },
        },
        new(KColor, DashOptKind.Color, Lang.T("dash_opt_value_color")) { Default = "auto", Auto = o => o.Choice(KLook, "number", Looks) == "number" ? Theme.Text : Theme.Accent },
        new(KDial, DashOptKind.Choice, Lang.T("dash_opt_dial"))
        {
            Default = "seg",
            Items = new[] { ("seg", Lang.T("dash_opt_dial_seg")), ("arc", Lang.T("dash_opt_dial_arc")) },
            EnabledWhen = o => o.Choice(KLook, "number", Looks) == "dial",
        },
        new(KScale, DashOptKind.Choice, Lang.T("dash_opt_pscale"))
        {
            Default = "auto",
            Items = Scales.Select(s => (s, s == "auto" ? Lang.T("dash_pw_auto") : s + " W")).ToArray(),
            EnabledWhen = o => o.Choice(KLook, "number", Looks) != "number",
        },
        new(KRange, DashOptKind.Choice, Lang.T("dash_opt_range"))
        {
            Default = "2",
            Items = Ranges.Select(m => (m, m + " min")).ToArray(),
            EnabledWhen = o => o.Choice(KLook, "number", Looks) == "trail",
        },
        LabelDef(Theme.Muted),
    };
}