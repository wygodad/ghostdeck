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

    /// <summary>Bars of the active-install history (the last bar in the accent), with the top value at the left.</summary>
    public static void Bars(Graphics g, Rectangle r, IReadOnlyList<(string week, int active)> history, DashContext c, bool labels)
    {
        if (history.Count == 0 || r.Width <= 0 || r.Height <= 0) return;
        var f = c.Font(8f);
        int labelH = labels ? f.Height + c.S(4) : 0;
        var plot = new Rectangle(r.Left, r.Top, r.Width, r.Height - labelH);
        int max = Math.Max(1, history.Max(h => h.active));
        int n = history.Count, gap = c.S(3);
        float bw = (plot.Width - gap * (n - 1)) / (float)n;
        using var track = new SolidBrush(Theme.Border);
        using var fill = new SolidBrush(Color.FromArgb(150, Theme.Accent));
        using var last = new SolidBrush(Theme.Accent);
        for (int i = 0; i < n; i++)
        {
            float x = plot.Left + i * (bw + gap);
            float h = Math.Max(c.S(2), plot.Height * history[i].active / (float)max);
            var bar = new RectangleF(x, plot.Bottom - h, Math.Max(1f, bw), h);
            using var path = Theme.RoundRect(bar, Math.Min(bw / 2f, c.Sf(3)));
            g.FillPath(i == n - 1 ? last : fill, path);
            if (labels && (i == 0 || i == n - 1 || (n > 8 && i % Math.Max(1, n / 6) == 0 && i < n - 2)))
            {
                string w = history[i].week.Length >= 8 ? "W" + history[i].week[^2..] : history[i].week;
                var tr = new Rectangle((int)x - c.S(12), plot.Bottom + c.S(4), (int)bw + c.S(24), f.Height);
                TextRenderer.DrawText(g, w, f, tr, Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping);
            }
        }
        if (labels)
            TextRenderer.DrawText(g, max.ToString("N0", Lang.Culture), f, new Rectangle(plot.Left, plot.Top, plot.Width, f.Height), Theme.Faint,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);
    }

    /// <summary>A ranked list: name, a bar against the largest, the count at the right. Returns the rows drawn.</summary>
    public static int Ranked(Graphics g, Rectangle r, IReadOnlyList<(string name, int count)> rows, DashContext c, Color color, float nameShare = 0f)
    {
        if (rows.Count == 0) { DashPaint.Note(g, r, c, Lang.T("dash_no_data")); return 0; }
        var f = c.Font(9.5f);
        var nf = c.Font(9.5f, FontStyle.Bold);
        int rowH = f.Height + c.S(14), max = Math.Max(1, rows.Max(x => x.count));
        int countW = TextRenderer.MeasureText(max.ToString("N0", Lang.Culture), nf, Size.Empty, TextFormatFlags.NoPadding).Width + c.S(8);
        // the name column: a fixed share of the width when asked (long laptop names), else up to 150 px
        int nameW = nameShare > 0 ? (int)(r.Width * nameShare) : Math.Min(r.Width / 2, c.S(150));
        int drawn = 0;
        for (int i = 0; i < rows.Count && (i + 1) * rowH <= r.Height; i++, drawn++)
        {
            int y = r.Top + i * rowH;
            TextRenderer.DrawText(g, rows[i].name, f, new Rectangle(r.Left, y, nameW, rowH), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.WordEllipsis);   // dpi-ok: a long country name keeps its bar and count in view
            var bar = new RectangleF(r.Left + nameW + c.S(10), y + (rowH - c.S(8)) / 2f, r.Width - nameW - c.S(10) - countW, c.S(8));
            if (bar.Width > 0) DashPaint.Bar(g, bar, rows[i].count / (float)max, color);
            TextRenderer.DrawText(g, rows[i].count.ToString("N0", Lang.Culture), nf, new Rectangle(r.Right - countW, y, countW, rowH), Theme.Text,
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
    public CommunityWidget(DashWidgetDef def) : base(def.TypeId, def.NameKey, def.Sizes) { }

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c)
    {
        Frame(g, r);
        var inner = Inner(r, c);
        int th = DashPaint.Title(g, inner, c, Lang.T(NameKey));
        var body = new Rectangle(inner.Left, inner.Top + th + c.S(8), inner.Width, inner.Height - th - c.S(8));
        if (!c.D.Settings.StatsEnabled) { DashPaint.Note(g, body, c, Lang.T("dash_st_off_short")); return; }
        var sum = Stats.Current;
        if (sum == null) { DashPaint.Note(g, body, c, Lang.T("dash_st_waiting")); return; }
        bool tall = r.Height >= r.Width * 0.45f;   // 4x2 (two rows of cells under a title) against 4x1
        int tilesH = tall ? (body.Height - c.S(8)) / 2 : body.Height;
        var tiles = CommunityStats.Tiles(sum, c.D);
        int colW = body.Width / tiles.Length, colGap = c.S(12);
        var lf = c.Font(8f, FontStyle.Bold);
        var numF = c.PxFont(Math.Min(tilesH * 0.34f, c.Sf(26)), FontStyle.Bold);
        var nameF = c.Font(11.5f, FontStyle.Bold);
        var sf = c.Font(8.5f);
        const TextFormatFlags wrap = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak;
        // the labels line up: every column leaves room for the tallest label (two lines at most)
        int labelH = 0;
        foreach (var t in tiles)
            labelH = Math.Max(labelH, Math.Min(lf.Height * 2, TextRenderer.MeasureText(Lang.Upper(t.Label), lf, new Size(colW - colGap, int.MaxValue), wrap).Height));
        for (int i = 0; i < tiles.Length; i++)
        {
            var t = tiles[i];
            var col = new Rectangle(body.Left + i * colW, body.Top, colW - colGap, tilesH);
            bool isName = i >= 2;   // the most placed and the rising widget: names, not numbers
            var vf = isName ? nameF : numF;
            int y = col.Top + c.S(2);
            TextRenderer.DrawText(g, Lang.Upper(t.Label), lf, new Rectangle(col.Left, y, col.Width, labelH), Theme.Faint, wrap);
            y += labelH + c.S(4);
            int vh = isName ? Math.Min(vf.Height * 2, TextRenderer.MeasureText(t.Value, vf, new Size(col.Width, int.MaxValue), wrap).Height) : vf.Height;
            TextRenderer.DrawText(g, t.Value, vf, new Rectangle(col.Left, y, col.Width, vh), t.Accent ?? Theme.Text, isName ? wrap : TextFormatFlags.Left | TextFormatFlags.NoPadding);
            y += vh + c.S(2);
            if (t.Sub.Length > 0 && y + sf.Height <= col.Bottom)
                TextRenderer.DrawText(g, t.Sub, sf, new Rectangle(col.Left, y, col.Width, Math.Min(sf.Height * 2, col.Bottom - y)), Theme.Muted, wrap);
        }
        if (tall)
        {
            var chart = new Rectangle(body.Left, body.Top + tilesH + c.S(8), body.Width, body.Height - tilesH - c.S(8));
            CommunityStats.Bars(g, chart, sum.History, c, labels: chart.Height >= c.S(60));
        }
    }

    public override void Click(Point p, Rectangle r, DashContext c) => c.ShowStats();
}
