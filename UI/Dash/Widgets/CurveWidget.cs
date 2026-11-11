using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// Fan curve: the model's curve tables as the fan-curve page draws them in its playground
/// (index-spaced temperatures on X, speed on Y, CPU in accent, GPU in violet) with the live
/// operating point of each fan from the shared sample. The tables are read once on a worker
/// (dozens of WMI calls, like Status and the editor do it) and belong to the page, not to the
/// widget: every copy on the Dashboard, the catalogue's picture and the settings card's preview
/// draw from the one read (1.45). The chart is the static layer, the dots and the live line next
/// to the title are the live layer. A click opens the editor. On a model without curve tables
/// the card says so.
///
/// Settings (1.45): the title and its colour, which of the two lines to draw (never neither)
/// and the colour of each, and the trail of past temperatures: on or off, and its colour. A
/// single-fan model has one line and one colour to set.
/// </summary>
internal sealed class CurveWidget : DashWidget
{
    /// <summary>Preview tool only: a curve to draw when there is no hardware to read one from.</summary>
    internal static (int[] ct, int[] cs, int[] gt, int[] gs)? PreviewCurve { get; set; }

    /// <summary>The curve tables of one Dashboard page: read once, whoever asks first, and gone with the page.</summary>
    private sealed class Tables
    {
        public (int[] ct, int[] cs, int[] gt, int[] gs)? Curve;
        public bool Loading, Loaded;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DashContext, Tables> Shared = new();

    private DeviceProfile? _dev;
    private bool _devResolved;
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

    private const string KCpu = "cpu", KGpu = "gpu", KCpuColor = "cpuc", KGpuColor = "gpuc", KTrail = "trail", KTrailColor = "trailc";

    /// <summary>The colours of the CPU and the GPU line: accent and violet unless picked.</summary>
    private Color[] LineColors() => new[] { Tint(KCpuColor, Theme.Accent), Tint(KGpuColor, Theme.Violet) };

    /// <summary>Which of the two lines to draw: both unless one is switched off; a single-fan model has the first alone.</summary>
    private bool[] Lines()
    {
        if (SingleFan) return new[] { true, false };
        bool cpu = Opt.Flag(KCpu, true), gpu = Opt.Flag(KGpu, true);
        return cpu || gpu ? new[] { cpu, gpu } : new[] { true, true };
    }

    private Tables EnsureCurve(DashContext c)
    {
        var t = Shared.GetOrCreateValue(c);
        if (PreviewCurve != null) { t.Curve = PreviewCurve; t.Loaded = true; return t; }
        if (t.Loaded || t.Loading) return t;
        var dev = Device(c);
        if (dev?.FanCurve == null) { t.Loaded = true; return t; }
        t.Loading = true;
        var owner = c.Owner; var rebuild = c.RequestRebuild;
        Task.Run(() =>
        {
            (int[], int[], int[], int[])? cv = null;
            try { cv = Ec.ReadFanCurve(dev); } catch { }
            try { owner.BeginInvoke(() => { t.Curve = cv; t.Loaded = true; t.Loading = false; rebuild(); }); }
            catch { t.Loading = false; }
        });
        return t;
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
        DashPaint.Title(g, inner, c, Caption, Tint("labelc", Theme.Text));
        var dev = Device(c);
        int titleH = c.Font(11f, FontStyle.Bold).Height;
        var body = new Rectangle(inner.Left, inner.Top + titleH, inner.Width, inner.Height - titleH);
        if (dev?.FanCurve == null && PreviewCurve == null) { DashPaint.Note(g, body, c, Lang.T("dash_unavailable")); return; }
        var tables = EnsureCurve(c);
        if (tables.Curve == null) { if (tables.Loaded) DashPaint.Note(g, body, c, "-"); return; }

        var (ct, cs, gt, gs) = tables.Curve.Value;
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
        var cols = LineColors();
        var show = Lines();
        for (int fan = 0; fan < 2; fan++)
        {
            int[] sp = fan == 0 ? cs : gs;
            if (!show[fan] || sp.Length < 2) continue;
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
        for (int fan = 0; fan < 2; fan++)
        {
            if (!show[fan]) continue;
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
        if (!Shared.TryGetValue(c, out var tables) || tables.Curve == null) return;
        var (ct, cs, gt, gs) = tables.Curve.Value;
        var inner = Inner(r, c);
        var plot = Plot(inner, c, out _);
        int max = MaxPct;
        var cols = LineColors();
        var show = Lines();
        var s = c.Sample;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // the trail ring: one entry per new sample, three minutes deep
        if (s.TempsKnown && s.At != DateTime.MinValue && (_trail.Count == 0 || _trail[^1].at != s.At))
        {
            _trail.Add((s.At, s.Hw.CpuTemp, s.Hw.GpuTemp));
            var cut = s.At - TrailSpan;
            _trail.RemoveAll(e => e.at < cut);
        }
        // live line next to the title: the temperature and fan target of the CPU, or of the GPU when its line is the only one drawn
        int liveTemp = show[0] ? s.Hw.CpuTemp : s.Hw.GpuTemp, liveFan = show[0] ? s.Hw.CpuFan : s.Hw.GpuFan;
        if (s.TempsKnown && liveTemp > 0)
        {
            var f = c.Font(9f);
            string live = $"{liveTemp} °C · {Lang.T("fc_live_fan")} {liveFan} %";
            int titleW = TextRenderer.MeasureText(Caption, c.Font(11f, FontStyle.Bold), Size.Empty, TextFormatFlags.NoPadding).Width;
            int titleH = c.Font(11f, FontStyle.Bold).Height;
            TextRenderer.DrawText(g, live, f, new Rectangle(inner.Left + titleW + c.S(10), inner.Top, Math.Max(10, inner.Width - titleW - c.S(10)), titleH), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        bool trailOn = Opt.Flag(KTrail, true);
        var trailColor = Tint(KTrailColor, Theme.Amber);
        var bubbles = new List<Rectangle>();   // placed bubbles, so the GPU's never covers the CPU's
        for (int fan = 0; fan < 2; fan++)
        {
            int[] temps = fan == 0 ? ct : gt, sp = fan == 0 ? cs : gs;
            int t = fan == 0 ? s.Hw.CpuTemp : s.Hw.GpuTemp;
            if (!show[fan] || !s.TempsKnown || t <= 0 || temps.Length < 2 || sp.Length != temps.Length) continue;
            PointF OnCurve(int temp) => new(plot.Left + CurveModel.OrdinalX(temps, temp) * plot.Width / (temps.Length - 1),
                                            plot.Bottom - Math.Clamp(CurveModel.SpeedAt(temps, sp, temp), 0, max) / (float)max * plot.Height);
            // the trail (amber unless picked, and it can be switched off): where this fan's temperature has been, older = fainter and smaller
            int cnt = trailOn ? _trail.Count : 0;
            for (int i = 0; i < cnt - 1; i++)
            {
                int qt = fan == 0 ? _trail[i].cpu : _trail[i].gpu;
                if (qt <= 0) continue;
                float age = i / (float)Math.Max(1, cnt - 1);   // 0 = oldest, 1 = newest
                float rr = c.Sf(1.6f + 1.4f * age);
                var q = OnCurve(qt);
                using var tb = new SolidBrush(Color.FromArgb(40 + (int)(150 * age), trailColor));
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

    public override IReadOnlyList<DashOptionDef> OptionDefs(DashContext c)
    {
        Device(c);
        var list = new List<DashOptionDef>();
        if (SingleFan)
            list.Add(new(KCpuColor, DashOptKind.Color, Lang.T("dash_opt_line_color")) { Default = "auto", Auto = _ => Theme.Accent });
        list.Add(LabelDef(Theme.Text));
        if (!SingleFan)
        {
            // a switch per line with the line's colour beside it; the last one on stays on
            list.Add(new(KCpu, DashOptKind.Toggle, Lang.T("fc_fan_cpu")) { DefaultOn = true, KeepOne = "lines", Row = "lines", ColorKey = KCpuColor, Auto = _ => Theme.Accent });
            list.Add(new(KGpu, DashOptKind.Toggle, Lang.T("fc_fan_gpu")) { DefaultOn = true, KeepOne = "lines", Row = "lines", ColorKey = KGpuColor, Auto = _ => Theme.Violet });
        }
        // the dots that show where the temperature has been over the last minutes
        list.Add(new(KTrail, DashOptKind.Toggle, Lang.T("dash_opt_trail")) { DefaultOn = true, ColorKey = KTrailColor, Auto = _ => Theme.Amber });
        return list;
    }
}
