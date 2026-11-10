namespace GhostDeck;

/// <summary>
/// The five gauge widgets (CPU and GPU temperature, CPU and GPU fan target, CPU usage): the
/// Status ring in a card. Static layer: the card, the dim track and the label. Live layer: the
/// lit part, the value and, for the fans, the RPM line. A click opens the Status tab.
///
/// Settings (1.45): the dial's colour, its style (segments or a continuous arc), the label and
/// its colour, the warnings (temperatures: two thresholds, usage: one; each turns the dial to a
/// colour of its own, and "No warnings" switches them off), the scale (temperatures), the RPM
/// line and its colour (fans). With nothing set a temperature and a fan paint exactly as in
/// 1.44; the usage dial is the same until the load reaches its warning threshold.
/// </summary>
internal sealed class RingWidget : DashWidget
{
    public enum Kind { CpuTemp, GpuTemp, CpuFan, GpuFan, CpuUsage }

    private const string KColor = "color", KDial = "dial", KLabel = "label", KLabelColor = "labelc",
                         KNoWarn = "nowarn", KMin = "min", KMax = "max", KRpm = "rpm", KRpmColor = "rpmc";
    private static readonly string[] Dials = { "seg", "arc" };

    /// <summary>One warning: from <see cref="Def"/> (unless set) the dial takes this level's colour, amber or red unless picked.</summary>
    private readonly record struct Level(string Key, string ColorKey, int Def, string LabelKey, bool Amber)
    {
        public Color Own => Amber ? Theme.Amber : Theme.Red;
    }

    // temperatures: amber from 70 and red from 85, as the Status rings; usage: red from 90
    private static readonly Level[] TempLevels = { new("warn1", "warn1c", 70, "dash_opt_warn1", true), new("warn2", "warn2c", 85, "dash_opt_warn2", false) };
    private static readonly Level[] UsageLevels = { new("warn1", "warn1c", 90, "dash_opt_warn", false) };
    private const int WarnTempMin = 40, WarnTempMax = 120, WarnUseMin = 10, WarnUseMax = 100;
    // the scale's ends and the least span between them
    private const int ScaleMinLo = 0, ScaleMinHi = 80, ScaleMaxLo = 50, ScaleMaxHi = 130, ScaleSpan = 20;

    private readonly Kind _kind;
    private readonly string _color, _label, _labelColor, _rpmColor;
    private readonly bool _arc, _rpmLine;
    private readonly int _min, _max;
    private readonly (int At, string Color, Level Level)[] _warn;   // the highest threshold first; empty = no warnings
    private string _painted = "";

    public RingWidget(DashWidgetDef def, DashOptions opt, Kind kind) : base(def, opt)
    {
        _kind = kind;
        _color = opt.Choice(KColor, "auto", DashPaint.PaletteChoices);
        _arc = opt.Choice(KDial, "seg", Dials) == "arc";
        _label = opt.Text(KLabel);
        _labelColor = opt.Choice(KLabelColor, "auto", DashPaint.PaletteChoices);
        _rpmLine = opt.Flag(KRpm, true);
        _rpmColor = opt.Choice(KRpmColor, "auto", DashPaint.PaletteChoices);
        (_min, _max) = IsTemp ? Scale(opt) : (0, IsFan ? 150 : 100);
        _warn = opt.Flag(KNoWarn, false)
            ? Array.Empty<(int, string, Level)>()
            : Levels.Select(l => (opt.Int(l.Key, l.Def, WarnMin, WarnMax), opt.Choice(l.ColorKey, "auto", DashPaint.PaletteChoices), l))
                    .OrderByDescending(w => w.Item1).ToArray();
    }

    private bool IsFan => _kind is Kind.CpuFan or Kind.GpuFan;
    private bool IsTemp => _kind is Kind.CpuTemp or Kind.GpuTemp;
    private Level[] Levels => IsTemp ? TempLevels : _kind == Kind.CpuUsage ? UsageLevels : Array.Empty<Level>();
    private int WarnMin => IsTemp ? WarnTempMin : WarnUseMin;
    private int WarnMax => IsTemp ? WarnTempMax : WarnUseMax;

    /// <summary>The dial's colour when no warning applies and none is picked.</summary>
    private Color Own => _kind switch
    {
        Kind.CpuFan => Theme.Accent,
        Kind.GpuFan => Theme.Violet,
        Kind.CpuUsage => DashPaint.CpuUseColor,
        _ => Theme.Green,
    };

    /// <summary>The scale's ends; a span too short to read falls back to 0-100.</summary>
    private static (int min, int max) Scale(DashOptions o)
    {
        int min = o.Int(KMin, 0, ScaleMinLo, ScaleMinHi), max = o.Int(KMax, 100, ScaleMaxLo, ScaleMaxHi);
        return max - min >= ScaleSpan ? (min, max) : (0, 100);
    }

    /// <summary>The dial's colour for a value: the colour of the highest warning reached, else the dial's colour.</summary>
    private Color ColorFor(int value)
    {
        foreach (var w in _warn)
            if (value >= w.At) return DashPaint.Pick(w.Color, w.Level.Own);
        return DashPaint.Pick(_color, Own);
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
        TextRenderer.DrawText(g, Label, c.Font(10f), label, DashPaint.Pick(_labelColor, Theme.Muted),
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
            TextRenderer.DrawText(g, rpm > 0 ? $"{rpm} RPM" : "- RPM", c.Font(9.5f, FontStyle.Bold), sub, DashPaint.Pick(_rpmColor, color),
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
        var list = new List<DashOptionDef>
        {
            new(KColor, DashOptKind.Color, Lang.T("dash_opt_dial_color")) { Default = "auto", Auto = _ => Own },
            new(KDial, DashOptKind.Choice, Lang.T("dash_opt_dial"))
            {
                Default = "seg",
                Items = new[] { ("seg", Lang.T("dash_opt_dial_seg")), ("arc", Lang.T("dash_opt_dial_arc")) },
            },
            new(KLabel, DashOptKind.Text, Lang.T("dash_opt_label")) { Placeholder = Lang.T(NameKey), ColorKey = KLabelColor, Auto = _ => Theme.Muted },
        };
        if (Levels.Length > 0)
        {
            // the switch first: the thresholds under it dim when it is on, and nothing above it moves
            list.Add(new(KNoWarn, DashOptKind.Toggle, Lang.T("dash_opt_nowarn")));
            foreach (var l in Levels)
            {
                var level = l;
                list.Add(new(l.Key, DashOptKind.Number, Lang.T(l.LabelKey))
                {
                    Min = WarnMin, Max = WarnMax, DefaultInt = l.Def, Unit = IsTemp ? "°C" : "%",
                    ColorKey = l.ColorKey, Auto = _ => level.Own,
                    EnabledWhen = o => !o.Flag(KNoWarn, false),
                });
            }
        }
        if (IsTemp)
        {
            list.Add(new(KMin, DashOptKind.Number, Lang.T("dash_opt_scale_from")) { Min = ScaleMinLo, Max = ScaleMinHi, DefaultInt = 0, Unit = "°C" });
            list.Add(new(KMax, DashOptKind.Number, Lang.T("dash_opt_scale_to"))
            {
                Min = ScaleMaxLo, Max = ScaleMaxHi, DefaultInt = 100, Unit = "°C",
                Rule = o => o.Int(KMax, 100, ScaleMaxLo, ScaleMaxHi) - o.Int(KMin, 0, ScaleMinLo, ScaleMinHi) >= ScaleSpan,
            });
        }
        if (IsFan)
            // the speed line is in the dial's colour unless it has one of its own
            list.Add(new(KRpm, DashOptKind.Toggle, Lang.T("dash_opt_rpm"))
            {
                DefaultOn = true, ColorKey = KRpmColor,
                Auto = o => DashPaint.Pick(o.Choice(KColor, "auto", DashPaint.PaletteChoices), Own),
            });
        return list;
    }
}
