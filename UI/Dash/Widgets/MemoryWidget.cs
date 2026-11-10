namespace GhostDeck;

/// <summary>
/// Memory and disks: the Status rows (name, "used / total GB · %", a bar) for RAM, VRAM and as
/// many disks as the card has room for. Everything in the rows is live, so the static layer is
/// the card alone.
/// </summary>
internal sealed class MemoryWidget : DashWidget
{
    private string _painted = "";

    public MemoryWidget(DashWidgetDef def, DashOptions opt) : base(def, opt) { }

    private sealed record Row(string Name, string Value, float Frac, Color Color, bool HasBar);

    private static List<Row> Rows(DashSample s)
    {
        var rows = new List<Row>();
        rows.Add(new Row(Lang.T("st_ram"),
            s.RamTotalGb > 0 ? $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0.0} GB · {s.RamPercent}%" : "-",
            s.RamPercent / 100f, s.RamPercent >= 90 ? Theme.Amber : Theme.Accent, s.RamTotalGb > 0));
        if (s.VramTotalMb > 0 && s.VramUsedMb >= 0)
        {
            int pct = (int)Math.Round(100.0 * s.VramUsedMb / s.VramTotalMb);
            rows.Add(new Row(Lang.T("ov_m_vram"), $"{s.VramUsedMb / 1024f:0.0} / {s.VramTotalMb / 1024f:0.0} GB · {pct}%",
                pct / 100f, pct >= 90 ? Theme.Amber : Theme.AccentFill, true));
        }
        foreach (var d in s.Disks)
        {
            string cap = d.VolGb > 0 ? $"{d.UsedGb:0} / {d.SizeGb:0} GB" : d.SizeGb >= 1000 ? $"{d.SizeGb / 1000:0.0} TB" : $"{d.SizeGb:0} GB";
            string val = cap + " · " + (d.TempC > 0 ? $"{d.TempC} °C" : "-");
            float frac = d.VolGb > 0 ? (float)Math.Clamp(d.UsedGb / d.VolGb, 0, 1) : 0;
            rows.Add(new Row(d.Name, val, frac, d.TempC >= 70 ? Theme.Amber : frac >= 0.9f ? Theme.Amber : Theme.AccentFill, d.VolGb > 0));
        }
        return rows;
    }

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c) => Frame(g, r);

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        var inner = Inner(r, c);
        var nameF = c.Font(10.5f, FontStyle.Bold);
        var valF = c.Font(10.5f);
        int lineH = Math.Max(c.S(26), nameF.Height + 2), barH = c.S(10), barGap = c.S(6), rowGap = c.S(12);
        int y = inner.Top;
        foreach (var row in Rows(c.Sample))
        {
            int valW = TextRenderer.MeasureText(row.Value, valF, Size.Empty, TextFormatFlags.NoPadding).Width + c.S(6);
            int nameW = TextRenderer.MeasureText(row.Name, nameF, Size.Empty, TextFormatFlags.NoPadding).Width;
            bool two = nameW + c.S(10) + valW > inner.Width;   // a long disk name: the value moves to its own line
            int block = (two ? lineH * 2 : lineH) + (row.HasBar ? barGap + barH : 0) + rowGap;
            if (y + block - rowGap > inner.Bottom) break;
            TextRenderer.DrawText(g, row.Name, nameF, new Rectangle(inner.Left, y, two ? inner.Width : inner.Width - valW - c.S(10), lineH), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
            int vy = two ? y + lineH : y;
            TextRenderer.DrawText(g, row.Value, valF, new Rectangle(inner.Right - valW, vy, valW, lineH), Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (row.HasBar) DashPaint.Bar(g, new RectangleF(inner.Left, vy + lineH + barGap, inner.Width, barH), row.Frac, row.Color);
            y += block;
        }
        _painted = Signature(c.Sample);
    }

    private static string Signature(DashSample s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var row in Rows(s)) sb.Append(row.Name).Append('|').Append(row.Value).Append('|').Append(row.Color.ToArgb()).Append(';');
        return sb.ToString();
    }

    public override bool LiveChanged(DashContext c) => Signature(c.Sample) != _painted;
    public override void Click(Point p, Rectangle r, DashContext c) => c.OpenTab(MainTab.Status);
}
