namespace GhostDeck;

/// <summary>
/// Memory and disks: the Status rows (name, "used / total GB · %", a bar) for RAM, VRAM and as
/// many disks as the card has room for. Everything in the rows is live, so the static layer is
/// the card alone.
///
/// Settings (1.45): which rows to show and the colour of the bars (a full or hot one stays
/// amber). One picked row fits a single cell.
/// </summary>
internal sealed class MemoryWidget : DashWidget
{
    private string _painted = "";

    private const string KShow = "show", KColor = "color";

    public MemoryWidget(DashWidgetDef def, DashOptions opt) : base(def, opt)
    {
        if (opt.List(KShow) is { Count: 1 }) Sizes = Sz((1, 1), (2, 1));
    }

    // Id: "ram", "vram", or "disk:" and the disk's name (two disks of one model go together)
    private sealed record Row(string Id, string Name, string Value, float Frac, Color Color, bool HasBar);

    /// <summary>Every row this machine has now, with the bars in the picked colour (amber stays the sign of full or hot).</summary>
    private List<Row> AllRows(DashSample s)
    {
        string pick = Opt.Choice(KColor, "auto", DashPaint.PaletteChoices);
        Color Bar(Color own, bool warn) => warn ? Theme.Amber : DashPaint.Pick(pick, own);
        var rows = new List<Row>();
        rows.Add(new Row("ram", Lang.T("st_ram"),
            s.RamTotalGb > 0 ? $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0.0} GB · {s.RamPercent}%" : "-",
            s.RamPercent / 100f, Bar(Theme.Accent, s.RamPercent >= 90), s.RamTotalGb > 0));
        if (s.VramTotalMb > 0 && s.VramUsedMb >= 0)
        {
            int pct = (int)Math.Round(100.0 * s.VramUsedMb / s.VramTotalMb);
            rows.Add(new Row("vram", Lang.T("ov_m_vram"), $"{s.VramUsedMb / 1024f:0.0} / {s.VramTotalMb / 1024f:0.0} GB · {pct}%",
                pct / 100f, Bar(Theme.AccentFill, pct >= 90), true));
        }
        foreach (var d in s.Disks)
        {
            string cap = d.VolGb > 0 ? $"{d.UsedGb:0} / {d.SizeGb:0} GB" : d.SizeGb >= 1000 ? $"{d.SizeGb / 1000:0.0} TB" : $"{d.SizeGb:0} GB";
            string val = cap + " · " + (d.TempC > 0 ? $"{d.TempC} °C" : "-");
            float frac = d.VolGb > 0 ? (float)Math.Clamp(d.UsedGb / d.VolGb, 0, 1) : 0;
            rows.Add(new Row("disk:" + d.Name, d.Name, val, frac, Bar(Theme.AccentFill, d.TempC >= 70 || frac >= 0.9f), d.VolGb > 0));
        }
        return rows;
    }

    /// <summary>The rows to show: all of them unless some were picked.</summary>
    private IReadOnlyList<Row> Rows(DashSample s) => Picked(KShow, AllRows(s), r => r.Id);

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c) => Frame(g, r);

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        var inner = Inner(r, c);
        var nameF = c.Font(10.5f, FontStyle.Bold);
        var valF = c.Font(10.5f);
        int lineH = Math.Max(c.S(26), nameF.Height + 2), barH = c.S(10), barGap = c.S(6), rowGap = c.S(12);
        int y = inner.Top;
        var rows = Rows(c.Sample);
        _painted = Signature(c.Sample);
        if (rows.Count == 0) { DashPaint.Note(g, inner, c, Lang.T("dash_sel_none")); return; }   // the picked disk is not there now
        foreach (var row in rows)
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
    }

    private string Signature(DashSample s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var row in Rows(s)) sb.Append(row.Name).Append('|').Append(row.Value).Append('|').Append(row.Color.ToArgb()).Append(';');
        return sb.ToString();
    }

    public override bool LiveChanged(DashContext c) => Signature(c.Sample) != _painted;
    public override void Click(Point p, Rectangle r, DashContext c) => c.OpenTab(MainTab.Status);

    public override IReadOnlyList<DashOptionDef> OptionDefs(DashContext c)
    {
        var all = AllRows(c.Sample);
        var list = new List<DashOptionDef>();
        if (all.Count > 1) list.Add(new(KShow, DashOptKind.Checks, Lang.T("dash_opt_show")) { Items = all.Select(r => (r.Id, r.Name)).ToArray() });
        list.Add(new(KColor, DashOptKind.Color, Lang.T("dash_opt_bar_color")) { Default = "auto", Auto = _ => Theme.AccentFill });
        return list;
    }
}
