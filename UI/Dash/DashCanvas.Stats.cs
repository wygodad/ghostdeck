using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// The Statystyki view (plan 7f): what the GhostDeck community does this week, from the signed
/// weekly summary. Four headline tiles, the 26-week bars, the dot map with the countries list,
/// then languages and board families. With statistics off the view explains itself and offers
/// the switch (the third way into the consent); with them on and no summary yet, it waits.
/// </summary>
internal sealed partial class DashCanvas
{
    private Rectangle _mapRect;   // where the dots are, for the hover label
    private Point _mouse;

    private sealed record StatsLayout(Rectangle[] Tiles, Rectangle Chart, Rectangle Map, Rectangle Countries, Rectangle Langs, Rectangle Models, int Bottom);

    private StatsLayout LayoutStats(int width)
    {
        int left = Pad, top = GridTop(), w = width - Pad * 2;
        bool wide = w >= S(900);
        int cols = wide ? 4 : 2, tileH = S(104);
        var tiles = new Rectangle[4];
        int tw = (w - Gap * (cols - 1)) / cols;
        for (int i = 0; i < 4; i++) tiles[i] = new Rectangle(left + (i % cols) * (tw + Gap), top + (i / cols) * (tileH + Gap), tw, tileH);
        int y = tiles[3].Bottom + Gap;
        var chart = new Rectangle(left, y, w, S(230));
        y = chart.Bottom + Gap;
        Rectangle map, countries;
        if (wide)
        {
            int mw = (int)(w * 0.62);
            map = new Rectangle(left, y, mw, S(360));
            countries = new Rectangle(left + mw + Gap, y, w - mw - Gap, S(360));
            y = map.Bottom + Gap;
        }
        else
        {
            map = new Rectangle(left, y, w, S(300));
            countries = new Rectangle(left, map.Bottom + Gap, w, S(320));
            y = countries.Bottom + Gap;
        }
        int half = (w - Gap) / 2;
        var langs = new Rectangle(left, y, half, S(250));
        var models = new Rectangle(left + half + Gap, y, w - half - Gap, S(250));
        return new StatsLayout(tiles, chart, map, countries, langs, models, models.Bottom);
    }

    private int StatsViewHeight(int width)
    {
        if (!_d.Settings.StatsEnabled) return S(230);
        if (Summary == null) return S(170);
        return LayoutStats(width).Bottom - GridTop();
    }

    /// <summary>A card with a title; returns the body rectangle under the title.</summary>
    private Rectangle StatsCard(Graphics g, Rectangle r, string title)
    {
        Ui.FillCard(g, r);
        var inner = Rectangle.Inflate(r, -S(16), -S(16));
        var f = _ctx.Font(11f, FontStyle.Bold);
        TextRenderer.DrawText(g, title, f, new Rectangle(inner.Left, inner.Top, inner.Width, f.Height), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.NoPadding);
        return new Rectangle(inner.Left, inner.Top + f.Height + S(10), inner.Width, inner.Height - f.Height - S(10));
    }

    private void PaintStatsView(Graphics g)
    {
        int w = Width - Pad * 2;
        if (!_d.Settings.StatsEnabled)
        {
            // the explanation and the switch: the same words as the consent card's gain line
            var card = new Rectangle(Pad, GridTop(), w, S(200));
            var body = StatsCard(g, card, Lang.T("dash_st_off_title"));
            var tf = _ctx.Font(10f);
            TextRenderer.DrawText(g, Lang.T("dash_st_off_body"), tf, new Rectangle(body.Left, body.Top, body.Width, tf.Height * 3), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            int by = body.Top + tf.Height * 3 + S(14);
            var bf = ButtonFont();
            int bh = ButtonH;
            int chrome = S(12) + S(16) + S(8) + S(14);   // PaintButton's glyph slot and paddings
            int w1 = TextRenderer.MeasureText(Lang.T("dash_st_on_btn"), bf, Size.Empty, TextFormatFlags.NoPadding).Width + chrome;
            var b1 = new Rectangle(body.Left, by, w1, bh);
            PaintButton(g, b1, "", Lang.T("dash_st_on_btn"), primary: true, active: false, hot: false);
            _zones.Add((b1, new Hot(Zone.StatsOn, -1, null)));
            int w2 = TextRenderer.MeasureText(Lang.T("set_stats_info"), bf, Size.Empty, TextFormatFlags.NoPadding).Width + chrome;
            var b2 = new Rectangle(b1.Right + S(10), by, w2, bh);
            PaintButton(g, b2, "", Lang.T("set_stats_info"), primary: false, active: false, hot: false);
            _zones.Add((b2, new Hot(Zone.StatsInfo, -1, null)));
            return;
        }
        var sum = Summary;
        if (sum == null)
        {
            var card = new Rectangle(Pad, GridTop(), w, S(140));
            Ui.FillCard(g, card);
            DashPaint.Note(g, Rectangle.Inflate(card, -S(16), -S(16)), _ctx, Lang.T("dash_st_waiting"));
            return;
        }
        var lay = LayoutStats(Width);

        // ---- four headline tiles ----
        var tiles = CommunityStats.Tiles(sum, _d);
        var lf = _ctx.Font(8f, FontStyle.Bold);
        var vf = _ctx.Font(18f, FontStyle.Bold);
        var sf = _ctx.Font(9f);
        for (int i = 0; i < 4; i++)
        {
            var r = lay.Tiles[i];
            Ui.FillCard(g, r);
            var inner = Rectangle.Inflate(r, -S(16), -S(14));
            TextRenderer.DrawText(g, Lang.Upper(tiles[i].Label), lf, new Rectangle(inner.Left, inner.Top, inner.Width, lf.Height), Theme.Faint,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, tiles[i].Value, vf, new Rectangle(inner.Left, inner.Top + lf.Height + S(4), inner.Width, vf.Height), tiles[i].Accent ?? Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.WordEllipsis);   // dpi-ok: a long widget name in a narrow tile keeps the tile's shape
            if (tiles[i].Sub.Length > 0)
                TextRenderer.DrawText(g, tiles[i].Sub, sf, new Rectangle(inner.Left, inner.Top + lf.Height + S(4) + vf.Height + S(2), inner.Width, sf.Height), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }

        // ---- the 26-week bars ----
        {
            var body = StatsCard(g, lay.Chart, string.Format(Lang.T("dash_st_history"), string.Format(Lang.T("dash_st_week_fmt"), sum.Week)));
            CommunityStats.Bars(g, body, sum.History, _ctx, labels: true);
        }

        // ---- the map and the countries ----
        {
            var body = StatsCard(g, lay.Map, Lang.T("dash_st_map"));
            var nf = _ctx.Font(8.5f);
            int noteH = nf.Height + S(6);
            _mapRect = new Rectangle(body.Left, body.Top, body.Width, body.Height - noteH);
            var counts = sum.Countries.ToDictionary(c => c.code, c => c.count);
            WorldMap.Instance.Paint(g, _mapRect, counts, Theme.BorderStrong, Theme.Accent, null);
            TextRenderer.DrawText(g, Lang.T("dash_st_map_note"), nf, new Rectangle(body.Left, body.Bottom - nf.Height, body.Width, nf.Height), Theme.Faint,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            _zones.Add((_mapRect, new Hot(Zone.Map, -1, null)));
            var list = StatsCard(g, lay.Countries, Lang.T("dash_st_countries"));
            CommunityStats.Ranked(g, list, sum.Countries.Select(c => (WorldMap.CountryName(c.code), c.count)).ToList(), _ctx, Theme.Accent);
        }

        // ---- languages and board families ----
        {
            var body = StatsCard(g, lay.Langs, Lang.T("dash_st_langs"));
            CommunityStats.Ranked(g, body, sum.Langs.Select(l => (WorldMap.LanguageName(l.code), l.count)).ToList(), _ctx, Theme.Violet);
            var mb = StatsCard(g, lay.Models, Lang.T("dash_st_models"));
            CommunityStats.Ranked(g, mb, sum.Models.Select(m => (m.prefix, m.count)).ToList(), _ctx, Theme.Green);
        }
    }

    /// <summary>The hover label over the map: country and count under the cursor (live layer).</summary>
    private void PaintMapHover(Graphics g)
    {
        if (_hot.Kind != Zone.Map || Summary is not { } sum) return;
        var code = WorldMap.Instance.Hit(_mapRect, _mouse);
        if (code == null) return;
        int n = sum.Countries.FirstOrDefault(c => c.code == code).count;
        string text = WorldMap.CountryName(code) + (n > 0 ? " · " + n.ToString("N0", Lang.Culture) : "");
        var f = _ctx.Font(9f, FontStyle.Bold);
        var sz = TextRenderer.MeasureText(text, f, Size.Empty, TextFormatFlags.NoPadding);
        var r = new Rectangle(_mouse.X + S(14), _mouse.Y - sz.Height - S(14), sz.Width + S(16), sz.Height + S(8));
        if (r.Right > _mapRect.Right) r.X = _mouse.X - r.Width - S(6);
        if (r.Top < _mapRect.Top) r.Y = _mouse.Y + S(14);
        using (var path = Theme.RoundRect(r, Sf(6)))
        {
            using var b = new SolidBrush(Theme.Surface);
            g.FillPath(b, path);
            using var pen = new Pen(n > 0 ? Theme.Accent : Theme.BorderStrong);
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, text, f, r, n > 0 ? Theme.Accent : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void TurnStatsOn()
    {
        var s = _d.Settings;
        s.StatsEnabled = true;
        s.StatsAsked = true;
        try { _d.SaveSettings(); } catch { }
        _d.StatsChanged?.Invoke();
        Replace();
        ContentChanged?.Invoke();
    }

    private void ShowStatsInfo()
    {
        new StatsConsentCard(() => { }, () =>
        {
            System.Text.Json.Nodes.JsonObject? report = null;
            string? error = null;
            try { report = Stats.BuildReport(_d.Settings, _d.Firmware(), _d.AppVersion(), _d.Current(), DateTime.Now); }
            catch (Exception ex) { error = ex.GetType().Name; }
            new StatsReportCard(report, error).Show();
        }, infoOnly: true).ShowOver(FindForm());
    }
}
