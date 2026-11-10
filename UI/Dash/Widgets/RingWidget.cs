namespace GhostDeck;

/// <summary>
/// The five gauge widgets (CPU and GPU temperature, CPU and GPU fan target, CPU usage): the
/// Status ring in a card. Static layer: the card, the dim track and the label. Live layer: the
/// lit part, the value and, for the fans, the RPM line. A click opens the Status tab.
///
/// Settings (1.45): the colour (automatic or one of the palette), the dial style (segments or
/// a continuous arc), the label, a warning threshold from which the dial is red (temperatures,
/// usage), the scale (temperatures) and the RPM line (fans). With nothing set the widget paints
/// exactly as in 1.44.
/// </summary>
internal sealed class RingWidget : DashWidget
{
    public enum Kind { CpuTemp, GpuTemp, CpuFan, GpuFan, CpuUsage }

    private const string KColor = "color", KDial = "dial", KLabel = "label", KWarn = "warn", KMin = "min", KMax = "max", KRpm = "rpm";
    private static readonly string[] Dials = { "seg", "arc" };
    // temperatures: the threshold 40-120 °C (85 unless set, 0 = off), the scale's ends, and the least span between them
    private const int WarnTempMin = 40, WarnTempMax = 120, WarnTempDef = 85;
    private const int ScaleMinLo = 0, ScaleMinHi = 80, ScaleMaxLo = 50, ScaleMaxHi = 130, ScaleSpan = 20;
    private const int WarnUseMin = 10, WarnUseMax = 100;

    private readonly Kind _kind;
    private readonly string _color, _label;
    private readonly bool _arc, _rpmLine;
    private readonly int _warn, _min, _max;
    private string _painted = "";

    public RingWidget(DashWidgetDef def, DashOptions opt, Kind kind) : base(def, opt)
    {
        _kind = kind;
        _color = opt.Choice(KColor, "auto", DashPaint.PaletteChoices);
        _arc = opt.Choice(KDial, "seg", Dials) == "arc";
        _label = opt.Text(KLabel);
        _rpmLine = opt.Flag(KRpm, true);
        _warn = IsTemp ? Threshold(opt, WarnTempDef, WarnTempMin, WarnTempMax) : _kind == Kind.CpuUsage ? Threshold(opt, 0, WarnUseMin, WarnUseMax) : 0;
        (_min, _max) = IsTemp ? Scale(opt) : (0, IsFan ? 150 : 100);
    }

    private bool IsFan => _kind is Kind.CpuFan or Kind.GpuFan;
    private bool IsTemp => _kind is Kind.CpuTemp or Kind.GpuTemp;

    /// <summary>The warning threshold: 0 = off, else within its range.</summary>
    private static int Threshold(DashOptions o, int def, int min, int max)
    {
        int v = o.Int(KWarn, def, 0, max);
        return v == 0 ? 0 : Math.Max(min, v);
    }

    /// <summary>The scale's ends; a span too short to read falls back to 0-100.</summary>
    private static (int min, int max) Scale(DashOptions o)
    {
        int min = o.Int(KMin, 0, ScaleMinLo, ScaleMinHi), max = o.Int(KMax, 100, ScaleMaxLo, ScaleMaxHi);
        return max - min >= ScaleSpan ? (min, max) : (0, 100);
    }

    /// <summary>The dial's colour for a value: red from the warning threshold, else the picked colour, else the kind's own.</summary>
    private Color ColorFor(int value)
    {
        if (_warn > 0 && value >= _warn) return Theme.Red;
        if (_color != "auto") return DashPaint.PaletteColor(_color);
        return _kind switch
        {
            // green, amber from 15 degrees under the threshold, red from the threshold: 70 and 85 unless set
            Kind.CpuTemp or Kind.GpuTemp => value <= 0 ? Theme.Muted : _warn > 0 && value >= _warn - 15 ? Theme.Amber : Theme.Green,
            Kind.CpuFan => Theme.Accent,
            Kind.GpuFan => Theme.Violet,
            _ => DashPaint.CpuUseColor,
        };
    }

    /// <summary>Value, colour and whether the reading means anything, decided as the Status rings decide it.</summary>
    private (int value, Color color, bool known, string unit, int rpm) Read(DashSample s) => _kind switch
    {
        Kind.CpuTemp  => (s.Hw.CpuTemp, ColorFor(s.Hw.CpuTemp), s.TempsKnown && s.Hw.CpuTemp > 0 && s.Hw.CpuTemp <= 129, "°C", 0),
        Kind.GpuTemp  => (s.Hw.GpuTemp, ColorFor(s.Hw.GpuTemp), s.TempsKnown && s.Hw.GpuTemp > 0 && s.Hw.GpuTemp <= 129, "°C", 0),
        Kind.CpuFan   => (s.Hw.CpuFan, ColorFor(s.Hw.CpuFan), s.FansKnown && s.Hw.CpuFan >= 0 && s.Hw.CpuFan <= 150, "%", s.Hw.CpuRpm),
        Kind.GpuFan   => (s.Hw.GpuFan, ColorFor(s.Hw.GpuFan), s.FansKnown && s.Hw.GpuFan >= 0 && s.Hw.GpuFan <= 150, "%", s.Hw.GpuRpm),
        _             => (s.CpuUsage, ColorFor(s.CpuUsage), s.CpuUsage >= 0, "%", 0),
    };

    private string Label => _label.Length > 0 ? _label : Lang.T(NameKey);

    private (RectangleF box, Rectangle label, Rectangle sub) Layout(Rectangle r, DashContext c)
    {
        var inner = Inner(r, c);
        var lf = c.Font(10f);
        int labelH = TextRenderer.MeasureText(Label, lf, new Size(inner.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
        labelH = Math.Min(labelH, lf.Height * 2);
        int subH = IsFan && _rpmLine ? c.Font(9.5f, FontStyle.Bold).Height : 0;
        int gap = c.S(10);
        int d = Math.Max(c.S(40), Math.Min(inner.Width, inner.Height - gap - labelH - subH));
        int total = d + gap + labelH + subH;
        int top = inner.Top + Math.Max(0, (inner.Height - total) / 2);
        var box = new RectangleF(inner.Left + (inner.Width - d) / 2f, top, d, d);
        var label = new Rectangle(inner.Left, top + d + gap, inner.Width, labelH);
        var sub = new Rectangle(inner.Left, label.Bottom, inner.Width, subH);
        return (box, label, sub);
    }

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c)
    {
        Frame(g, r);
        var (box, label, _) = Layout(r, c);
        if (_arc) DashPaint.RingTrackArc(g, box); else DashPaint.RingTrack(g, box);
        TextRenderer.DrawText(g, Label, c.Font(10f), label, Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        var (value, color, known, unit, rpm) = Read(c.Sample);
        var (box, _, sub) = Layout(r, c);
        if (known)
        {
            float fraction = Math.Clamp((value - _min) / (float)(_max - _min), 0, 1);
            if (_arc) DashPaint.RingLitArc(g, box, fraction, color); else DashPaint.RingLit(g, box, fraction, color);
        }
        DashPaint.RingValue(g, box, known ? value.ToString() : "-", unit, c);
        if (IsFan && _rpmLine && known)
            TextRenderer.DrawText(g, rpm > 0 ? $"{rpm} RPM" : "- RPM", c.Font(9.5f, FontStyle.Bold), sub, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        _painted = Signature(c.Sample);
    }

    private string Signature(DashSample s)
    {
        var (value, color, known, _, rpm) = Read(s);
        return $"{value}|{known}|{color.ToArgb()}|{rpm}";
    }

    public override bool LiveChanged(DashContext c) => Signature(c.Sample) != _painted;

    public override void Click(Point p, Rectangle r, DashContext c) => c.OpenTab(MainTab.Status);

    public override IReadOnlyList<DashOptionDef> OptionDefs(DashContext c)
    {
        var auto = _kind switch
        {
            Kind.CpuTemp or Kind.GpuTemp => new[] { Theme.Green, Theme.Amber, Theme.Red },
            Kind.CpuFan => new[] { Theme.Accent },
            Kind.GpuFan => new[] { Theme.Violet },
            _ => new[] { DashPaint.CpuUseColor },
        };
        var list = new List<DashOptionDef>
        {
            new(KColor, DashOptKind.Color, Lang.T("dash_opt_color")) { Default = "auto", AutoColors = auto, AutoHint = Lang.T(IsTemp ? "dash_opt_auto_temp" : "dash_opt_auto") },
            new(KDial, DashOptKind.Choice, Lang.T("dash_opt_dial"))
            {
                Default = "seg",
                Items = new[] { ("seg", Lang.T("dash_opt_dial_seg")), ("arc", Lang.T("dash_opt_dial_arc")) },
            },
            new(KLabel, DashOptKind.Text, Lang.T("dash_opt_label")) { Placeholder = Lang.T(NameKey) },
        };
        if (IsTemp)
        {
            list.Add(new(KWarn, DashOptKind.Number, Lang.T("dash_opt_warn"))
                { Min = WarnTempMin, Max = WarnTempMax, DefaultInt = WarnTempDef, EmptyValue = 0, Unit = "°C", Hint = Lang.T("dash_opt_warn_hint") });
            list.Add(new(KMin, DashOptKind.Number, Lang.T("dash_opt_scale_from")) { Min = ScaleMinLo, Max = ScaleMinHi, DefaultInt = 0, Unit = "°C" });
            list.Add(new(KMax, DashOptKind.Number, Lang.T("dash_opt_scale_to"))
            {
                Min = ScaleMaxLo, Max = ScaleMaxHi, DefaultInt = 100, Unit = "°C",
                Rule = o => o.Int(KMax, 100, ScaleMaxLo, ScaleMaxHi) - o.Int(KMin, 0, ScaleMinLo, ScaleMinHi) >= ScaleSpan,
            });
        }
        else if (_kind == Kind.CpuUsage)
            list.Add(new(KWarn, DashOptKind.Number, Lang.T("dash_opt_warn"))
                { Min = WarnUseMin, Max = WarnUseMax, DefaultInt = 0, EmptyValue = 0, Unit = "%", Hint = Lang.T("dash_opt_warn_hint") });
        else
            list.Add(new(KRpm, DashOptKind.Toggle, Lang.T("dash_opt_rpm")) { DefaultOn = true });
        return list;
    }
}
