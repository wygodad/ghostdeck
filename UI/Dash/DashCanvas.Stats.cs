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

    // ---- the entrance: bars grow and the install count counts up over half a second ----
    // The static layer leaves the bars and the four values out while this runs; the live layer
    // draws them at the eased progress and the static layer takes over once it is done. Only the
    // chart and the tiles are invalidated per frame, nothing else repaints.
    private float _statsAnim = 1f;
    private DateTime _statsAnimStart;
    private System.Windows.Forms.Timer? _statsTimer;
    private Rectangle _chartBody;
    private IReadOnlyList<(string week, int active)> _chartHist = Array.Empty<(string, int)>();
    private readonly List<(Rectangle value, CommunityStats.Tile tile)> _tileLive = new();
    private readonly List<(Rectangle body, List<(string name, int count)> rows, Color color, float nameShare)> _listBodies = new();
    private Dictionary<string, int> _mapCounts = new();
    private const int EntranceMs = 520;

    /// <summary>Everything the entrance repaints per frame: the chart, the tiles, the lists and the map.</summary>
    private Rectangle StatsDirty()
    {
        var d = _chartBody;
        void Add(Rectangle r) { if (r.IsEmpty) return; d = d.IsEmpty ? r : Rectangle.Union(d, r); }
        foreach (var (r, _) in _tileLive) Add(r);
        foreach (var l in _listBodies) Add(l.body);
        Add(_mapRect);
        return d;
    }

    /// <summary>A ranked list on the static layer (skipped while the entrance runs), remembered for the live layer and the row hover.</summary>
    private void RankedList(Graphics g, Rectangle body, List<(string name, int count)> rows, Color color, float nameShare, bool entering)
    {
        int li = _listBodies.Count;
        _listBodies.Add((body, rows, color, nameShare));
        if (!entering) CommunityStats.Ranked(g, body, rows, _ctx, color, nameShare);
        int rowH = CommunityStats.RankedRowH(_ctx), fit = Math.Min(rows.Count, body.Height / Math.Max(1, rowH));
        for (int i = 0; i < fit; i++)
            _zones.Add((new Rectangle(body.Left - S(8), body.Top + i * rowH, body.Width + S(16), rowH), new Hot(Zone.Row, li * 1000 + i, null)));
    }

    private void StartStatsEntrance()
    {
        _statsTimer ??= NewStatsTimer();
        _statsAnim = 0f;
        _statsAnimStart = DateTime.UtcNow;
        _statsTimer.Start();
    }

    private System.Windows.Forms.Timer NewStatsTimer()
    {
        var t = new System.Windows.Forms.Timer { Interval = 16 };
        t.Tick += (_, _) =>
        {
            float x = (float)((DateTime.UtcNow - _statsAnimStart).TotalMilliseconds / EntranceMs);
            if (x >= 1f || _view != View.Stats || IsDisposed)
            {
                _statsAnim = 1f;
                t.Stop();
                if (!IsDisposed) Rebuild();   // the static layer takes the finished picture
                return;
            }
            _statsAnim = 1f - (1f - x) * (1f - x) * (1f - x);   // ease out
            var dirty = StatsDirty();
            if (!dirty.IsEmpty) Invalidate(Rectangle.Inflate(dirty, S(10), S(4)));
        };
        return t;
    }

    private bool StatsAnimating => _view == View.Stats && _statsAnim < 1f;

    /// <summary>The live part of the Statystyki view: the entrance frames, then the hover highlight of a bar.</summary>
    private void PaintStatsLive(Graphics g)
    {
        if (_chartHist.Count == 0) return;
        if (StatsAnimating)
        {
            float t = _statsAnim;
            CommunityStats.Bars(g, _chartBody, _chartHist, _ctx, labels: true, grow: t);
            foreach (var l in _listBodies) CommunityStats.Ranked(g, l.body, l.rows, _ctx, l.color, l.nameShare, grow: t);
            if (!_mapRect.IsEmpty) WorldMap.Instance.Paint(g, _mapRect, _mapCounts, Theme.BorderStrong, Theme.Accent, null, lit: t, landDots: false);
            var vf = _ctx.Font(18f, FontStyle.Bold);
            for (int i = 0; i < _tileLive.Count; i++)
            {
                var (r, tile) = _tileLive[i];
                string value = i == 0 && Summary is { } sum ? ((int)Math.Round(sum.ActiveWeek * t)).ToString("N0", Lang.Culture)
                             : t >= 0.35f ? tile.Value : "";
                if (value.Length > 0)
                    TextRenderer.DrawText(g, value, vf, r, tile.Accent ?? Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.WordEllipsis);   // dpi-ok: same rectangle as the static tile
            }
            return;
        }
        if (_hot.Kind == Zone.Row)
        {
            int li = _hot.Index / 1000, row = _hot.Index % 1000;
            if (li >= 0 && li < _listBodies.Count) { var l = _listBodies[li]; CommunityStats.Ranked(g, l.body, l.rows, _ctx, l.color, l.nameShare, lit: row); }
            return;
        }
        if (_hot.Kind != Zone.Bar || _hot.Index < 0 || _hot.Index >= _chartHist.Count) return;
        // the hovered bar in white, its week and count in a small label above it
        CommunityStats.Bars(g, _chartBody, _chartHist, _ctx, labels: true, lit: _hot.Index);
        var rects = CommunityStats.BarRects(_chartBody, _chartHist, _ctx, labels: true);
        var bar = rects[_hot.Index];
        var (week, active) = _chartHist[_hot.Index];
        string text = (week.Length >= 8 ? "W" + week[^2..] : week) + " · " + active.ToString("N0", Lang.Culture);
        var f = _ctx.Font(9f, FontStyle.Bold);
        var sz = TextRenderer.MeasureText(text, f, Size.Empty, TextFormatFlags.NoPadding);
        var box = new Rectangle((int)(bar.X + bar.Width / 2) - sz.Width / 2 - S(8), (int)bar.Y - sz.Height - S(16), sz.Width + S(16), sz.Height + S(8));
        if (box.Left < _chartBody.Left) box.X = _chartBody.Left;
        if (box.Right > _chartBody.Right) box.X = _chartBody.Right - box.Width;
        if (box.Top < _chartBody.Top) box.Y = (int)bar.Y + S(8);
        using (var path = Theme.RoundRect(box, Sf(6)))
        {
            using var b = new SolidBrush(Theme.Surface);
            g.FillPath(b, path);
            using var pen = new Pen(Theme.Accent);
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, text, f, box, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

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

        // ---- four headline tiles (the values come from the live layer while the entrance runs) ----
        bool entering = StatsAnimating;
        var tiles = CommunityStats.Tiles(sum, _d);
        var lf = _ctx.Font(8f, FontStyle.Bold);
        var vf = _ctx.Font(18f, FontStyle.Bold);
        var sf = _ctx.Font(9f);
        _tileLive.Clear();
        _listBodies.Clear();
        for (int i = 0; i < 4; i++)
        {
            var r = lay.Tiles[i];
            Ui.FillCard(g, r);
            _zones.Add((r, new Hot(Zone.Tile, i, null)));   // a hover outlines the tile
            var inner = Rectangle.Inflate(r, -S(16), -S(14));
            TextRenderer.DrawText(g, Lang.Upper(tiles[i].Label), lf, new Rectangle(inner.Left, inner.Top, inner.Width, lf.Height), Theme.Faint,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            var vr = new Rectangle(inner.Left, inner.Top + lf.Height + S(4), inner.Width, vf.Height);
            _tileLive.Add((vr, tiles[i]));
            if (!entering)
                TextRenderer.DrawText(g, tiles[i].Value, vf, vr, tiles[i].Accent ?? Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.WordEllipsis);   // dpi-ok: a long widget name in a narrow tile keeps the tile's shape
            if (tiles[i].Sub.Length > 0)
                TextRenderer.DrawText(g, tiles[i].Sub, sf, new Rectangle(inner.Left, inner.Top + lf.Height + S(4) + vf.Height + S(2), inner.Width, sf.Height), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }

        // ---- the 26-week bars (grown by the live layer on entrance; a hover lights one) ----
        {
            _chartBody = StatsCard(g, lay.Chart, string.Format(Lang.T("dash_st_history"), string.Format(Lang.T("dash_st_week_fmt"), sum.Week)));
            _chartHist = sum.History;
            if (!entering) CommunityStats.Bars(g, _chartBody, sum.History, _ctx, labels: true);
            var rects = CommunityStats.BarRects(_chartBody, sum.History, _ctx, labels: true);
            for (int i = 0; i < rects.Count; i++)
                _zones.Add((new Rectangle((int)rects[i].X - S(1), _chartBody.Top, (int)rects[i].Width + S(3), _chartBody.Height), new Hot(Zone.Bar, i, null)));
        }

        // ---- the map and the countries ----
        {
            var body = StatsCard(g, lay.Map, Lang.T("dash_st_map"));
            var nf = _ctx.Font(8.5f);
            int noteH = nf.Height + S(6);
            _mapRect = new Rectangle(body.Left, body.Top, body.Width, body.Height - noteH);
            _mapCounts = sum.Countries.ToDictionary(c => c.code, c => c.count);
            WorldMap.Instance.Paint(g, _mapRect, _mapCounts, Theme.BorderStrong, Theme.Accent, null, lit: entering ? 0f : 1f);
            TextRenderer.DrawText(g, Lang.T("dash_st_map_note"), nf, new Rectangle(body.Left, body.Bottom - nf.Height, body.Width, nf.Height), Theme.Faint,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            _zones.Add((_mapRect, new Hot(Zone.Map, -1, null)));
            var list = StatsCard(g, lay.Countries, Lang.T("dash_st_countries"));
            RankedList(g, list, sum.Countries.Select(c => (WorldMap.CountryName(c.code), c.count)).ToList(), Theme.Accent, 0f, entering);
        }

        // ---- languages and board families ----
        {
            var body = StatsCard(g, lay.Langs, Lang.T("dash_st_langs"));
            RankedList(g, body, sum.Langs.Select(l => (WorldMap.LanguageName(l.code), l.count)).ToList(), Theme.Violet, 0f, entering);
            var mb = StatsCard(g, lay.Models, Lang.T("dash_st_models"));
            RankedList(g, mb, sum.Models.Select(m => (CommunityStats.ModelName(m.prefix), m.count)).ToList(), Theme.Green, 0.62f, entering);
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
