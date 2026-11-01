namespace GhostDeck;

/// <summary>
/// The five gauge widgets (CPU and GPU temperature, CPU and GPU fan target, CPU usage): the
/// Status ring in a card. Static layer: the card, the dim track and the label. Live layer: the
/// lit ticks, the value and, for the fans, the RPM line. A click opens the Status tab.
/// </summary>
internal sealed class RingWidget : DashWidget
{
    public enum Kind { CpuTemp, GpuTemp, CpuFan, GpuFan, CpuUsage }

    private readonly Kind _kind;
    private string _painted = "";

    public RingWidget(DashWidgetDef def, Kind kind) : base(def.TypeId, def.NameKey, def.Sizes) { _kind = kind; }

    private bool IsFan => _kind is Kind.CpuFan or Kind.GpuFan;

    /// <summary>Value, scale, colour and whether the reading means anything, decided as the Status rings decide it.</summary>
    private (int value, int max, Color color, bool known, string unit, int rpm) Read(DashSample s) => _kind switch
    {
        Kind.CpuTemp  => (s.Hw.CpuTemp, 100, DashPaint.TempColor(s.Hw.CpuTemp), s.TempsKnown && s.Hw.CpuTemp > 0 && s.Hw.CpuTemp <= 129, "°C", 0),
        Kind.GpuTemp  => (s.Hw.GpuTemp, 100, DashPaint.TempColor(s.Hw.GpuTemp), s.TempsKnown && s.Hw.GpuTemp > 0 && s.Hw.GpuTemp <= 129, "°C", 0),
        Kind.CpuFan   => (s.Hw.CpuFan, 150, Theme.Accent, s.FansKnown && s.Hw.CpuFan >= 0 && s.Hw.CpuFan <= 150, "%", s.Hw.CpuRpm),
        Kind.GpuFan   => (s.Hw.GpuFan, 150, Theme.Violet, s.FansKnown && s.Hw.GpuFan >= 0 && s.Hw.GpuFan <= 150, "%", s.Hw.GpuRpm),
        _             => (s.CpuUsage, 100, DashPaint.CpuUseColor, s.CpuUsage >= 0, "%", 0),
    };

    private (RectangleF box, Rectangle label, Rectangle sub) Layout(Rectangle r, DashContext c)
    {
        var inner = Inner(r, c);
        var lf = c.Font(10f);
        string name = Lang.T(NameKey);
        int labelH = TextRenderer.MeasureText(name, lf, new Size(inner.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
        labelH = Math.Min(labelH, lf.Height * 2);
        int subH = IsFan ? c.Font(9.5f, FontStyle.Bold).Height : 0;
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
        DashPaint.RingTrack(g, box);
        TextRenderer.DrawText(g, Lang.T(NameKey), c.Font(10f), label, Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        var (value, max, color, known, unit, rpm) = Read(c.Sample);
        var (box, _, sub) = Layout(r, c);
        if (known) DashPaint.RingLit(g, box, Math.Clamp(value / (float)max, 0, 1), color);
        DashPaint.RingValue(g, box, known ? value.ToString() : "-", unit, c);
        if (IsFan && known)
            TextRenderer.DrawText(g, rpm > 0 ? $"{rpm} RPM" : "- RPM", c.Font(9.5f, FontStyle.Bold), sub, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        _painted = Signature(c.Sample);
    }

    private string Signature(DashSample s)
    {
        var (value, _, color, known, _, rpm) = Read(s);
        return $"{value}|{known}|{color.ToArgb()}|{rpm}";
    }

    public override bool LiveChanged(DashContext c) => Signature(c.Sample) != _painted;

    public override void Click(Point p, Rectangle r, DashContext c) => c.OpenTab(MainTab.Status);
}
