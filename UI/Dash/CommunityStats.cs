using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// The community numbers as the Statystyki view and the Community tile show them, from the
/// weekly summary: the four headline tiles, the 26-week bars and the ranked lists. One place to
/// word them, so the tile is always a true excerpt of the view.
/// </summary>
internal static class CommunityStats
{
    public readonly record struct Tile(string Label, string Value, string Sub, Color? Accent);

    /// <summary>Active installs with the change, widgets typical / yours, the most placed widget, the fastest rising.</summary>
    public static Tile[] Tiles(Stats.Summary sum, MainDeps d)
    {
        var cul = Lang.Culture;
        string trend = "";
        if (sum.ActivePrevWeek > 0)
        {
            double pct = 100.0 * (sum.ActiveWeek - sum.ActivePrevWeek) / sum.ActivePrevWeek;
            trend = string.Format(Lang.T("dash_st_vs_prev_fmt"), (pct >= 0 ? "+" : "") + Math.Round(pct).ToString(cul) + " %");
        }
        int yours = d.Settings.Dashboard.Count;
        string topType = sum.Top.Count > 0 ? sum.Top[0] : "";
        string topName = Name(topType), topSub = "";
        if (topType.Length > 0 && sum.Widgets.TryGetValue(topType, out var ts))
            topSub = string.Format(Lang.T("dash_share_fmt"), (int)Math.Round(ts.Share * 100));
        string risingName = Name(sum.Rising ?? ""), risingSub = "";
        if (sum.Rising != null && sum.Widgets.TryGetValue(sum.Rising, out var rs) && rs.Trend is { } tr)
            risingSub = string.Format(Lang.T("dash_st_points_fmt"), (tr >= 0 ? "+" : "") + Math.Round(tr * 100).ToString(cul));
        return new[]
        {
            new Tile(Lang.T("dash_st_active"), sum.ActiveWeek.ToString("N0", cul), trend, Theme.Accent),
            new Tile(Lang.T("dash_st_typical"), sum.TypicalWidgets > 0 ? $"{sum.TypicalWidgets} / {yours}" : yours.ToString(cul), "", null),
            new Tile(Lang.T("dash_st_top"), topName.Length > 0 ? topName : "-", topSub, null),
            new Tile(Lang.T("dash_st_rising"), risingName.Length > 0 ? risingName : "-", risingSub, Theme.Green),
        };
    }

    /// <summary>The widget's name for a type id; the id itself for a type this version does not know.</summary>
    public static string Name(string type) => type.Length == 0 ? "" : DashRegistry.Find(type) is { } def ? Lang.T(def.NameKey) : type;

    /// <summary>
    /// The laptop name for a board prefix from the report ("17S1" -> "Raider GE78HX 13V / Vector 17 HX
    /// A14V"), from the model database the app already carries; people remember the laptop, not the
    /// board. The prefix itself when no entry matches (a board this version does not know yet).
    /// </summary>
    public static string ModelName(string prefix)
    {
        if (prefix.Length == 0) return "";
        foreach (var p in Devices.All)
            foreach (var fw in p.FirmwarePrefixes)
                if (fw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string n = p.Name;
                    if (n.StartsWith("MSI ", StringComparison.OrdinalIgnoreCase)) n = n[4..];
                    return n;
                }
        return prefix;
    }

    /// <summary>The bar rectangles of <see cref="Bars"/> for a given area (hit testing and the hover highlight).</summary>
    public static List<RectangleF> BarRects(Rectangle r, IReadOnlyList<(string week, int active)> history, DashContext c, bool labels, float grow = 1f)
    {
        var rects = new List<RectangleF>(history.Count);
        if (history.Count == 0 || r.Width <= 0 || r.Height <= 0) return rects;
        var f = c.Font(8f);
        int labelH = labels ? f.Height + c.S(4) : 0, valueH = labels ? f.Height + c.S(4) : 0;   // room above the tallest bar for its value
        var plot = new Rectangle(r.Left, r.Top + valueH, r.Width, r.Height - labelH - valueH);
        int max = Math.Max(1, history.Max(h => h.active));
        int n = history.Count, gap = c.S(3);
        float bw = (plot.Width - gap * (n - 1)) / (float)n;
        for (int i = 0; i < n; i++)
        {
            float x = plot.Left + i * (bw + gap);
            float h = Math.Max(c.S(2), plot.Height * history[i].active / (float)max * grow);
            rects.Add(new RectangleF(x, plot.Bottom - h, Math.Max(1f, bw), h));
        }
        return rects;
    }

    /// <summary>
    /// Bars of the active-install history: the last bar in the accent with its value above it,
    /// week labels under every few bars. <paramref name="grow"/> below 1 draws the bars that much
    /// shorter (the entrance of the Statystyki view); <paramref name="lit"/> is the hovered bar.
    /// </summary>
    public static void Bars(Graphics g, Rectangle r, IReadOnlyList<(string week, int active)> history, DashContext c, bool labels, float grow = 1f, int lit = -1)
    {
        var rects = BarRects(r, history, c, labels, grow);
        if (rects.Count == 0) return;
        var f = c.Font(8f);
        var vf = c.Font(8.5f, FontStyle.Bold);
        int n = history.Count;
        int plotBottom = (int)Math.Round(rects[0].Bottom);
        using var fill = new SolidBrush(Color.FromArgb(150, Theme.Accent));
        using var last = new SolidBrush(Theme.Accent);
        using var hot = new SolidBrush(Color.White);
        for (int i = 0; i < n; i++)
        {
            var bar = rects[i];
            using var path = Theme.RoundRect(bar, Math.Min(bar.Width / 2f, c.Sf(3)));
            g.FillPath(i == lit ? hot : i == n - 1 ? last : fill, path);
            if (labels && (i == 0 || i == n - 1 || (n > 8 && i % Math.Max(1, n / 6) == 0 && i < n - 2)))
            {
                string w = history[i].week.Length >= 8 ? "W" + history[i].week[^2..] : history[i].week;
                var tr = new Rectangle((int)bar.X - c.S(12), plotBottom + c.S(4), (int)bar.Width + c.S(24), f.Height);
                TextRenderer.DrawText(g, w, f, tr, Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping);
            }
        }
        if (labels && grow >= 1f)
        {
            // the current week's value above its bar, never on it
            var lb = rects[n - 1];
            string v = history[n - 1].active.ToString("N0", Lang.Culture);
            int vw = TextRenderer.MeasureText(v, vf, Size.Empty, TextFormatFlags.NoPadding).Width + c.S(8);
            int vx = Math.Min((int)(lb.X + lb.Width / 2) - vw / 2, r.Right - vw);
            TextRenderer.DrawText(g, v, vf, new Rectangle(vx, (int)lb.Y - vf.Height - c.S(2), vw, vf.Height), Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping);
        }
    }

    /// <summary>Row height of <see cref="Ranked"/>; the rows that fit are the first (height / RowH).</summary>
    public static int RankedRowH(DashContext c) => c.Font(9.5f).Height + c.S(14);

    /// <summary>
    /// A ranked list: name, a bar against the largest, the count at the right. <paramref name="grow"/>
    /// below 1 draws the bars that much shorter (the entrance); <paramref name="lit"/> is the hovered
    /// row, drawn on a tinted band with its name in the accent. Returns the rows drawn.
    /// </summary>
    public static int Ranked(Graphics g, Rectangle r, IReadOnlyList<(string name, int count)> rows, DashContext c, Color color, float nameShare = 0f, float grow = 1f, int lit = -1)
    {
        if (rows.Count == 0) { DashPaint.Note(g, r, c, Lang.T("dash_no_data")); return 0; }
        var f = c.Font(9.5f);
        var nf = c.Font(9.5f, FontStyle.Bold);
        int rowH = RankedRowH(c), max = Math.Max(1, rows.Max(x => x.count));
        int countW = TextRenderer.MeasureText(max.ToString("N0", Lang.Culture), nf, Size.Empty, TextFormatFlags.NoPadding).Width + c.S(8);
        // the name column: a fixed share of the width when asked (long laptop names), else up to 150 px
        int nameW = nameShare > 0 ? (int)(r.Width * nameShare) : Math.Min(r.Width / 2, c.S(150));
        int drawn = 0;
        for (int i = 0; i < rows.Count && (i + 1) * rowH <= r.Height; i++, drawn++)
        {
            int y = r.Top + i * rowH;
            bool hot = i == lit;
            if (hot)
            {
                using var path = Theme.RoundRect(new Rectangle(r.Left - c.S(8), y, r.Width + c.S(16), rowH), c.Sf(6));
                using var b = new SolidBrush(Theme.AccentSoft);
                g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, rows[i].name, f, new Rectangle(r.Left, y, nameW, rowH), hot ? Theme.Accent : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.WordEllipsis);   // dpi-ok: a long country name keeps its bar and count in view
            var bar = new RectangleF(r.Left + nameW + c.S(10), y + (rowH - c.S(8)) / 2f, r.Width - nameW - c.S(10) - countW, c.S(8));
            if (bar.Width > 0) DashPaint.Bar(g, bar, rows[i].count / (float)max * grow, hot ? Color.White : color);
            TextRenderer.DrawText(g, rows[i].count.ToString("N0", Lang.Culture), nf, new Rectangle(r.Right - countW, y, countW, rowH), hot ? Theme.Accent : Theme.Text,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        return drawn;
    }
}

/// <summary>
/// The Community tile (plan 7g), 4x1 or 4x2: the four headline numbers of the Statystyki view
/// and, on the taller size, the 26-week bars. A click opens that view. Listed in the catalogue
/// only while statistics are on; placed and switched off, it says how to turn them back on.
/// </summary>
internal sealed class CommunityWidget : DashWidget
{
    public CommunityWidget(DashWidgetDef def, DashOptions opt) : base(def, opt) { }

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c)
    {
        Frame(g, r);
        var inner = Inner(r, c);
        int th = DashPaint.Title(g, inner, c, Lang.T(NameKey));
        var body = new Rectangle(inner.Left, inner.Top + th + c.S(8), inner.Width, inner.Height - th - c.S(8));
        if (!c.D.Settings.StatsEnabled) { DashPaint.Note(g, body, c, Lang.T("dash_st_off_short")); return; }
        var sum = Stats.Current;
        if (sum == null) { DashPaint.Note(g, body, c, Lang.T("dash_st_waiting")); return; }
        // four columns: label (up to two lines), value, one sub line; the block takes only the
        // height it needs, and whatever is left under it goes to the bars (4x2) or stays as air (4x1)
        var tiles = CommunityStats.Tiles(sum, c.D);
        int colW = body.Width / tiles.Length, colGap = c.S(12);
        var lf = c.Font(8f, FontStyle.Bold);
        var numF = c.Font(17f, FontStyle.Bold);
        var nameF = c.Font(11.5f, FontStyle.Bold);
        var sf = c.Font(8.5f);
        const TextFormatFlags wrap = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak;
        int labelH = 0, valueH = 0;
        for (int i = 0; i < tiles.Length; i++)
        {
            int w = colW - colGap;
            labelH = Math.Max(labelH, Math.Min(lf.Height * 2, TextRenderer.MeasureText(Lang.Upper(tiles[i].Label), lf, new Size(w, int.MaxValue), wrap).Height));
            valueH = Math.Max(valueH, i >= 2 ? Math.Min(nameF.Height * 2, TextRenderer.MeasureText(tiles[i].Value, nameF, new Size(w, int.MaxValue), wrap).Height) : numF.Height);
        }
        int subH = tiles.Any(t => t.Sub.Length > 0) ? sf.Height * 2 : 0;
        int blockH = labelH + c.S(4) + valueH + c.S(2) + subH;
        bool tall = r.Height >= r.Width * 0.45f;   // 4x2 against 4x1
        int top = tall ? body.Top : body.Top + Math.Max(0, (body.Height - blockH) / 2);
        for (int i = 0; i < tiles.Length; i++)
        {
            var t = tiles[i];
            var col = new Rectangle(body.Left + i * colW, top, colW - colGap, blockH);
            bool isName = i >= 2;   // the most placed and the rising widget: names, not numbers
            int y = col.Top;
            TextRenderer.DrawText(g, Lang.Upper(t.Label), lf, new Rectangle(col.Left, y, col.Width, labelH), Theme.Faint, wrap);
            y += labelH + c.S(4);
            TextRenderer.DrawText(g, t.Value, isName ? nameF : numF, new Rectangle(col.Left, y, col.Width, valueH), t.Accent ?? Theme.Text,
                isName ? wrap : TextFormatFlags.Left | TextFormatFlags.NoPadding);
            y += valueH + c.S(2);
            if (t.Sub.Length > 0) TextRenderer.DrawText(g, t.Sub, sf, new Rectangle(col.Left, y, col.Width, subH), Theme.Muted, wrap);
        }
        if (tall)
        {
            int chartTop = body.Top + blockH + c.S(14);
            var chart = new Rectangle(body.Left, chartTop, body.Width, body.Bottom - chartTop);
            if (chart.Height >= c.S(40)) CommunityStats.Bars(g, chart, sum.History, c, labels: chart.Height >= c.S(60));
        }
    }

    public override void Click(Point p, Rectangle r, DashContext c) => c.ShowStats();
}
