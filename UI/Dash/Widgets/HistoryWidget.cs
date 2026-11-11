using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// A chart of the last minutes (1.45), one of three kinds: temperatures, the fan curve target and
/// the fan speed, each with a line for the CPU and one for the GPU. They are the charts of the
/// Status tab's History view as widgets, and they draw from the same place: the in-memory history
/// the tray poll fills every three seconds for as long as the app runs (<see cref="HwHistory"/>,
/// sixty minutes, never on disk). A widget therefore reads no hardware and adds no read to any
/// tick; it repaints when the history has a new sample.
///
/// The frame and the title are the static layer; the scale, the lines and the values of now are
/// the live layer, because all of them move (the fan speed chart picks its ceiling from the peak
/// in view, as the Status chart does). The values of now stand right of the title when they fit
/// before the corner where the tile's menu button comes up, and under the title otherwise.
/// A sample without a reading (a sleeping graphics card, a fan that reports nothing) is a gap in
/// the line, and so is a pause in the samples (the laptop slept). A click opens the Status tab.
///
/// Settings: the time in view (5, 15, 30 or 60 minutes), which of the two lines to draw (never
/// neither) and the colour of each, the title and its colour. A single-fan model has one line in
/// the two fan charts.
/// </summary>
internal sealed class HistoryWidget : DashWidget
{
    public enum Kind { Temps, FanTarget, Rpm }

    /// <summary>Preview tool only: samples to draw instead of the app's history (the newest one is "now"), so a picture does not depend on when it is made.</summary>
    internal static List<HwSample>? PreviewData { get; set; }

    private const string KRange = "range", KCpu = "cpu", KGpu = "gpu", KCpuColor = "cpuc", KGpuColor = "gpuc";
    private static readonly string[] Ranges = { "5", "15", "30", "60" };
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(12);   // four missed polls: the line is not drawn across

    private readonly Kind _kind;
    private readonly int _minutes;
    private long _painted = -1;
    private DeviceProfile? _dev;
    private bool _devResolved;

    public HistoryWidget(DashWidgetDef def, DashOptions opt, Kind kind) : base(def, opt)
    {
        _kind = kind;
        _minutes = int.Parse(Opt.Choice(KRange, "15", Ranges), System.Globalization.CultureInfo.InvariantCulture);
    }

    private bool SingleFan(DashContext c)
    {
        if (_kind == Kind.Temps) return false;
        if (!_devResolved) { try { _dev = Devices.Detect(c.D.Firmware()); } catch { _dev = null; } _devResolved = true; }
        return _dev?.FanCurve?.SingleFan ?? false;
    }

    private Color[] LineColors() => new[] { Tint(KCpuColor, Theme.Accent), Tint(KGpuColor, Theme.Violet) };

    /// <summary>Which of the two lines to draw: both unless one is switched off; a single-fan model has the first alone in the fan charts.</summary>
    private bool[] Lines(DashContext c)
    {
        if (SingleFan(c)) return new[] { true, false };
        bool cpu = Opt.Flag(KCpu, true), gpu = Opt.Flag(KGpu, true);
        return cpu || gpu ? new[] { cpu, gpu } : new[] { true, true };
    }

    private string Unit => _kind switch { Kind.Temps => "°C", Kind.FanTarget => "%", _ => "" };
    private string AxisUnit => _kind switch { Kind.Temps => "°", Kind.FanTarget => "%", _ => "" };
    private int Steps => _kind == Kind.FanTarget ? 3 : 4;

    private int Value(HwSample s, int line) => _kind switch
    {
        Kind.Temps => line == 0 ? s.CpuTemp : s.GpuTemp,
        Kind.FanTarget => line == 0 ? s.CpuFan : s.GpuFan,
        _ => line == 0 ? s.CpuRpm : s.GpuRpm,
    };

    /// <summary>A reading worth a point, by the rule of the Status charts: a fan target may be zero, a temperature or a speed of zero is "no reading".</summary>
    private bool Valid(int v, int max) => _kind == Kind.FanTarget ? v >= 0 && v <= max : v > 0 && v <= max * 1.3f;

    /// <summary>The top of the scale: fixed for temperatures and the fan target (the scales of the Status rings), 500 above the peak in view for the speed.</summary>
    private int Ceiling(List<HwSample> data)
    {
        if (_kind == Kind.Temps) return 100;
        if (_kind == Kind.FanTarget) return 150;
        int peak = 0;
        foreach (var s in data) peak = Math.Max(peak, Math.Max(s.CpuRpm, s.GpuRpm));
        return Math.Max(2000, (peak + 500 + 499) / 500 * 500);
    }

    /// <summary>The samples in view, oldest first, and the moment the right edge stands for.</summary>
    private List<HwSample> Data(out DateTime now)
    {
        var span = TimeSpan.FromMinutes(_minutes);
        if (PreviewData is { Count: > 0 } pv)
        {
            now = pv[^1].Time;
            var cut = now - span;
            return pv.Where(s => s.Time >= cut).ToList();
        }
        now = DateTime.Now;
        return HwHistory.Window(span);
    }

    private const TextFormatFlags Wrapped = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

    /// <summary>
    /// The title as it is drawn: one line, or two when it does not fit beside the corner of the
    /// tile's menu button (the name is long in some languages, and a label is the user's own
    /// text); what needs more ends with three dots, as a scene's name does.
    /// </summary>
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
        var inner = Inner(r, c);
        var (text, box, _) = TitleBox(inner, c);
        TextRenderer.DrawText(g, text, c.Font(11f, FontStyle.Bold), new Rectangle(box.Left, box.Top, Math.Max(c.S(40), inner.Width - c.S(22)), box.Height), Tint("labelc", Theme.Text),
            TextFormatFlags.Left | TextFormatFlags.Top | Wrapped);
    }

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        _painted = HwHistory.Version;
        var inner = Inner(r, c);
        var titleF = c.Font(11f, FontStyle.Bold);
        var valF = c.Font(9f, FontStyle.Bold);
        var axisF = c.Font(8.5f);
        const TextFormatFlags Plain = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var data = Data(out DateTime now);
        int max = Ceiling(data);
        var show = Lines(c);
        var cols = LineColors();

        // ---- the values of now: right of the title when they fit before the menu corner, else under it ----
        // the place is decided by the widest text a value can be, so it does not jump as the numbers change
        string widest = _kind switch { Kind.Temps => "100", Kind.FanTarget => "150", _ => "9999" } + Unit;
        string[] names = { "CPU", "GPU" };
        int gap = c.S(12), need = -gap;
        for (int i = 0; i < 2; i++)
            if (show[i]) need += gap + TextRenderer.MeasureText(names[i] + " " + widest, valF, Size.Empty, Plain).Width;
        var (_, title, oneLine) = TitleBox(inner, c);
        bool beside = oneLine && title.Right + c.S(14) + need <= inner.Right - c.S(22);
        int top = title.Bottom + c.S(8);
        HwSample? last = data.Count > 0 ? data[^1] : null;
        string Now(int line)
        {
            int v = last is { } s ? Value(s, line) : -1;
            return names[line] + " " + (last != null && Valid(v, max) ? v + Unit : "--");
        }
        int vx = beside ? inner.Right - c.S(22) - need : inner.Left;
        int vy = beside ? inner.Top + (titleF.Height - valF.Height) / 2 : top;
        for (int i = 0; i < 2; i++)
        {
            if (!show[i]) continue;
            string text = Now(i);
            int slot = TextRenderer.MeasureText(names[i] + " " + widest, valF, Size.Empty, Plain).Width;
            TextRenderer.DrawText(g, text, valF, new Rectangle(vx, vy, slot + c.S(2), valF.Height), cols[i], TextFormatFlags.Left | TextFormatFlags.Top | Plain);
            vx += slot + gap;
        }
        if (!beside) top += valF.Height + c.S(6);

        // ---- the plot: a column for the scale on the left, a line for the time under it ----
        int axisW = TextRenderer.MeasureText(max + AxisUnit, axisF, Size.Empty, Plain).Width + c.S(6);
        int plotTop = top + axisF.Height / 2, plotBottom = inner.Bottom - axisF.Height - c.S(4);
        var plot = new Rectangle(inner.Left + axisW, plotTop, inner.Width - axisW - c.S(2), plotBottom - plotTop);
        if (plot.Height < c.S(24) || plot.Width < c.S(40)) return;

        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        using (var grid = new Pen(Theme.Border))
            for (int i = 0; i <= Steps; i++)
            {
                int v = max * i / Steps;
                int y = plot.Bottom - (int)Math.Round(i / (float)Steps * plot.Height);
                g.DrawLine(grid, plot.Left, y, plot.Right, y);
                TextRenderer.DrawText(g, v + AxisUnit, axisF, new Rectangle(inner.Left, y - axisF.Height / 2, axisW - c.S(6), axisF.Height), Theme.Faint,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | Plain);
            }
        string from = "-" + _minutes + " min", to = Lang.T("st_hist_now");
        int toW = TextRenderer.MeasureText(to, axisF, Size.Empty, Plain).Width;
        TextRenderer.DrawText(g, from, axisF, new Point(plot.Left, plot.Bottom + c.S(4)), Theme.Faint, Plain);
        TextRenderer.DrawText(g, to, axisF, new Point(plot.Right - toW, plot.Bottom + c.S(4)), Theme.Faint, Plain);

        if (data.Count == 0)
        {
            g.SmoothingMode = old;
            DashPaint.Note(g, plot, c, Lang.T("st_hist_empty"));
            return;
        }
        if (_kind == Kind.Rpm && PreviewData == null && !HwHistory.HasRpm && data.Count >= 10)
        {
            // samples keep coming and none carries a speed: this model reports none
            g.SmoothingMode = old;
            DashPaint.Note(g, plot, c, Lang.T("dash_unavailable"));
            return;
        }

        // ---- the lines: the GPU first, the CPU over it ----
        g.SmoothingMode = SmoothingMode.AntiAlias;
        double span = _minutes * 60.0;
        var start = now - TimeSpan.FromMinutes(_minutes);
        for (int line = 1; line >= 0; line--)
        {
            if (!show[line]) continue;
            using var pen = new Pen(cols[line], c.Sf(1.8f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var dot = new SolidBrush(cols[line]);
            var run = new List<PointF>();
            DateTime prev = DateTime.MinValue;
            void Flush()
            {
                if (run.Count >= 2) g.DrawLines(pen, run.ToArray());
                else if (run.Count == 1) g.FillEllipse(dot, run[0].X - c.Sf(1.5f), run[0].Y - c.Sf(1.5f), c.Sf(3), c.Sf(3));
                run.Clear();
            }
            foreach (var s in data)
            {
                int v = Value(s, line);
                if (!Valid(v, max)) { Flush(); prev = DateTime.MinValue; continue; }
                if (prev != DateTime.MinValue && s.Time - prev > Pause) Flush();
                prev = s.Time;
                float x = plot.Left + (float)(Math.Clamp((s.Time - start).TotalSeconds / span, 0, 1) * plot.Width);
                float y = plot.Bottom - Math.Clamp(v, 0, max) / (float)max * plot.Height;
                run.Add(new PointF(x, y));
            }
            Flush();
        }
        g.SmoothingMode = old;
    }

    public override bool LiveChanged(DashContext c) => HwHistory.Version != _painted;

    public override void Click(Point p, Rectangle r, DashContext c) => c.OpenTab(MainTab.Status);

    public override IReadOnlyList<DashOptionDef> OptionDefs(DashContext c)
    {
        var list = new List<DashOptionDef>
        {
            new(KRange, DashOptKind.Choice, Lang.T("dash_opt_range")) { Default = "15", Items = Ranges.Select(m => (m, m + " min")).ToArray() },
        };
        if (SingleFan(c))
            list.Add(new(KCpuColor, DashOptKind.Color, Lang.T("dash_opt_line_color")) { Default = "auto", Auto = _ => Theme.Accent });
        list.Add(LabelDef(Theme.Text));
        if (!SingleFan(c))
        {
            // a switch per line with the line's colour beside it; the last one on stays on
            list.Add(new(KCpu, DashOptKind.Toggle, "CPU") { DefaultOn = true, KeepOne = "lines", Row = "lines", ColorKey = KCpuColor, Auto = _ => Theme.Accent });
            list.Add(new(KGpu, DashOptKind.Toggle, "GPU") { DefaultOn = true, KeepOne = "lines", Row = "lines", ColorKey = KGpuColor, Auto = _ => Theme.Violet });
        }
        return list;
    }
}
