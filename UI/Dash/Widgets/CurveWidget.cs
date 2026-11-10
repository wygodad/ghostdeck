using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// Fan curve: the model's curve tables as the fan-curve page draws them in its playground
/// (index-spaced temperatures on X, speed on Y, CPU in accent, GPU in violet) with the live
/// operating point of each fan from the shared sample. The tables are read once on a worker
/// (dozens of WMI calls, like Status and the editor do it); the chart is the static layer, the
/// dots and the live line next to the title are the live layer. A click opens the editor.
/// On a model without curve tables the card says so.
/// </summary>
internal sealed class CurveWidget : DashWidget
{
    /// <summary>Preview tool only: a curve to draw when there is no hardware to read one from.</summary>
    internal static (int[] ct, int[] cs, int[] gt, int[] gs)? PreviewCurve { get; set; }

    private DeviceProfile? _dev;
    private bool _devResolved;
    private (int[] ct, int[] cs, int[] gt, int[] gs)? _curve;
    private bool _loading, _loaded;
    private string _painted = "";
    // where the temperatures have been over the last three minutes (one entry per tick), for the
    // amber trail along the curve, as the fan-curve page's "curve in action" draws it
    private readonly List<(DateTime at, int cpu, int gpu)> _trail = new();
    private static readonly TimeSpan TrailSpan = TimeSpan.FromMinutes(3);

    public CurveWidget(DashWidgetDef def, DashOptions opt) : base(def, opt) { }

    private DeviceProfile? Device(DashContext c)
    {
        if (!_devResolved) { try { _dev = Devices.Detect(c.D.Firmware()); } catch { _dev = null; } _devResolved = true; }
        return _dev;
    }

    private int MaxPct => _dev?.FanCurve?.MaxFanPct ?? 150;
    private bool SingleFan => _dev?.FanCurve?.SingleFan ?? false;

    private void EnsureCurve(DashContext c)
    {
        if (PreviewCurve != null) { _curve = PreviewCurve; _loaded = true; return; }
        if (_loaded || _loading) return;
        var dev = Device(c);
        if (dev?.FanCurve == null) { _loaded = true; return; }
        _loading = true;
        var owner = c.Owner; var rebuild = c.RequestRebuild;
        Task.Run(() =>
        {
            (int[], int[], int[], int[])? cv = null;
            try { cv = Ec.ReadFanCurve(dev); } catch { }
            try { owner.BeginInvoke(() => { _curve = cv; _loaded = true; _loading = false; rebuild(); }); }
            catch { _loading = false; }
        });
    }

    private Rectangle Plot(Rectangle inner, DashContext c, out Rectangle legend)
    {
        var axisF = c.Font(8.5f);
        int titleH = c.Font(11f, FontStyle.Bold).Height;
        int axisW = TextRenderer.MeasureText(MaxPct + "%", axisF, Size.Empty, TextFormatFlags.NoPadding).Width + c.S(4);
        int legendH = axisF.Height + c.S(6);
        legend = new Rectangle(inner.Left, inner.Bottom - legendH, inner.Width, legendH);
        return new Rectangle(inner.Left + axisW, inner.Top + titleH + c.S(10), inner.Width - axisW - c.S(6),
                             inner.Height - titleH - c.S(10) - axisF.Height - c.S(6) - legendH - c.S(6));
    }

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c)
    {
        Frame(g, r);
        var inner = Inner(r, c);
        DashPaint.Title(g, inner, c, Lang.T(NameKey));
        var dev = Device(c);
        int titleH = c.Font(11f, FontStyle.Bold).Height;
        var body = new Rectangle(inner.Left, inner.Top + titleH, inner.Width, inner.Height - titleH);
        if (dev?.FanCurve == null && PreviewCurve == null) { DashPaint.Note(g, body, c, Lang.T("dash_unavailable")); return; }
        EnsureCurve(c);
        if (_curve == null) { if (_loaded) DashPaint.Note(g, body, c, "-"); return; }

        var (ct, cs, gt, gs) = _curve.Value;
        var plot = Plot(inner, c, out var legend);
        var axisF = c.Font(8.5f);
        int max = MaxPct, n = ct.Length;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var grid = new Pen(Theme.Border))
            for (int v = 0; v <= max; v += 25)
            {
                float y = plot.Bottom - v / (float)max * plot.Height;
                g.DrawLine(grid, plot.Left, y, plot.Right, y);
                TextRenderer.DrawText(g, v + "%", axisF, new Rectangle(inner.Left, (int)y - axisF.Height / 2, plot.Left - inner.Left - c.S(4), axisF.Height), Theme.Faint,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        for (int i = 0; i < n; i++)
        {
            float x = plot.Left + i * plot.Width / (float)Math.Max(1, n - 1);
            TextRenderer.DrawText(g, ct[i] + "°", axisF, new Rectangle((int)x - c.S(20), plot.Bottom + c.S(4), c.S(40), axisF.Height), Theme.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        }
        int fans = SingleFan ? 1 : 2;
        var cols = new[] { Theme.Accent, Theme.Violet };
        for (int fan = 0; fan < fans; fan++)
        {
            int[] sp = fan == 0 ? cs : gs;
            if (sp.Length < 2) continue;
            var pts = new PointF[sp.Length];
            for (int i = 0; i < sp.Length; i++)
                pts[i] = new PointF(plot.Left + i * plot.Width / (float)(sp.Length - 1), plot.Bottom - Math.Clamp(sp[i], 0, max) / (float)max * plot.Height);
            using var pen = new Pen(Color.FromArgb(fan == 0 ? 255 : 210, cols[fan]), c.Sf(2.2f)) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, pts);
            float rad = c.Sf(3.5f);
            using var dotB = new SolidBrush(cols[fan]);
            foreach (var p in pts) g.FillEllipse(dotB, p.X - rad, p.Y - rad, rad * 2, rad * 2);
        }
        // legend
        int lx = legend.Left, ly = legend.Top + c.S(3);
        for (int fan = 0; fan < fans; fan++)
        {
            string label = Lang.T(fan == 0 ? (SingleFan ? "fc_fan_single" : "fc_fan_cpu") : "fc_fan_gpu");
            using (var b = new SolidBrush(cols[fan])) g.FillRectangle(b, lx, ly + axisF.Height / 2 - c.S(1), c.S(14), c.S(3));
            lx += c.S(18);
            int tw = TextRenderer.MeasureText(label, axisF, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, label, axisF, new Rectangle(lx, ly, tw + c.S(2), axisF.Height), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            lx += tw + c.S(16);
        }
    }

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        _painted = Signature(c.Sample);
        if (_curve == null) return;
        var (ct, cs, gt, gs) = _curve.Value;
        var inner = Inner(r, c);
        var plot = Plot(inner, c, out _);
        int max = MaxPct, fans = SingleFan ? 1 : 2;
        var cols = new[] { Theme.Accent, Theme.Violet };
        var s = c.Sample;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // the trail ring: one entry per new sample, three minutes deep
        if (s.TempsKnown && s.At != DateTime.MinValue && (_trail.Count == 0 || _trail[^1].at != s.At))
        {
            _trail.Add((s.At, s.Hw.CpuTemp, s.Hw.GpuTemp));
            var cut = s.At - TrailSpan;
            _trail.RemoveAll(e => e.at < cut);
        }
        // live line next to the title: CPU temperature and fan target
        if (s.TempsKnown && s.Hw.CpuTemp > 0)
        {
            var f = c.Font(9f);
            string live = $"{s.Hw.CpuTemp} °C · {Lang.T("fc_live_fan")} {s.Hw.CpuFan} %";
            int titleW = TextRenderer.MeasureText(Lang.T(NameKey), c.Font(11f, FontStyle.Bold), Size.Empty, TextFormatFlags.NoPadding).Width;
            int titleH = c.Font(11f, FontStyle.Bold).Height;
            TextRenderer.DrawText(g, live, f, new Rectangle(inner.Left + titleW + c.S(10), inner.Top, Math.Max(10, inner.Width - titleW - c.S(10)), titleH), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        var bubbles = new List<Rectangle>();   // placed bubbles, so the GPU's never covers the CPU's
        for (int fan = 0; fan < fans; fan++)
        {
            int[] temps = fan == 0 ? ct : gt, sp = fan == 0 ? cs : gs;
            int t = fan == 0 ? s.Hw.CpuTemp : s.Hw.GpuTemp;
            if (!s.TempsKnown || t <= 0 || temps.Length < 2 || sp.Length != temps.Length) continue;
            PointF OnCurve(int temp) => new(plot.Left + CurveModel.OrdinalX(temps, temp) * plot.Width / (temps.Length - 1),
                                            plot.Bottom - Math.Clamp(CurveModel.SpeedAt(temps, sp, temp), 0, max) / (float)max * plot.Height);
            // the amber trail: where this fan's temperature has been, older = fainter and smaller
            int cnt = _trail.Count;
            for (int i = 0; i < cnt - 1; i++)
            {
                int qt = fan == 0 ? _trail[i].cpu : _trail[i].gpu;
                if (qt <= 0) continue;
                float age = i / (float)Math.Max(1, cnt - 1);   // 0 = oldest, 1 = newest
                float rr = c.Sf(1.6f + 1.4f * age);
                var q = OnCurve(qt);
                using var tb = new SolidBrush(Color.FromArgb(40 + (int)(150 * age), Theme.Amber));
                g.FillEllipse(tb, q.X - rr, q.Y - rr, rr * 2, rr * 2);
            }
            var dot = OnCurve(t);
            using (var halo = new SolidBrush(Color.FromArgb(70, cols[fan]))) g.FillEllipse(halo, dot.X - c.Sf(11), dot.Y - c.Sf(11), c.Sf(22), c.Sf(22));
            using (var b = new SolidBrush(cols[fan])) g.FillEllipse(b, dot.X - c.Sf(5.5f), dot.Y - c.Sf(5.5f), c.Sf(11), c.Sf(11));
            g.FillEllipse(Brushes.White, dot.X - c.Sf(2), dot.Y - c.Sf(2), c.Sf(4), c.Sf(4));
            // the bubble: "72°C · fan 61%" beside the dot, inside the plot, below the dot when there is no room above
            int duty = fan == 0 ? s.Hw.CpuFan : s.Hw.GpuFan;
            var lf = c.Font(8.5f, FontStyle.Bold);
            string label = $"{t}°C · {Lang.T("fc_live_fan")} {duty}%";
            var sz = TextRenderer.MeasureText(label, lf, Size.Empty, TextFormatFlags.NoPadding);
            int lw = sz.Width + c.S(12), lh = sz.Height + c.S(6);
            int lx = (int)dot.X + c.S(12);
            if (lx + lw > plot.Right) lx = (int)dot.X - lw - c.S(12);
            if (lx < plot.Left) lx = plot.Left;
            int ly = (int)dot.Y - lh - c.S(9);
            if (ly < plot.Top) ly = (int)dot.Y + c.S(11);
            var box = new Rectangle(lx, ly, lw, lh);
            foreach (var other in bubbles) if (box.IntersectsWith(other)) box.Y = other.Bottom + c.S(4);
            bubbles.Add(box);
            using (var path = Theme.RoundRect(box, c.Sf(5)))
            {
                using var bb = new SolidBrush(Color.FromArgb(Theme.Dark ? 220 : 240, Theme.Card));
                g.FillPath(bb, path);
                using var bp = new Pen(Color.FromArgb(170, cols[fan]));
                g.DrawPath(bp, path);
            }
            TextRenderer.DrawText(g, label, lf, box, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private static string Signature(DashSample s) => $"{s.TempsKnown}|{s.Hw.CpuTemp}|{s.Hw.GpuTemp}|{s.Hw.CpuFan}|{s.Hw.GpuFan}|{s.At.Ticks}";   // every tick: the trail grows

    public override bool LiveChanged(DashContext c) => Signature(c.Sample) != _painted;

    public override void Click(Point p, Rectangle r, DashContext c) => c.OpenTab(MainTab.FanCurve);
}
