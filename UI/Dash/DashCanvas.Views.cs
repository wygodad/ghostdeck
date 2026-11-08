using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// The three views of the canvas (plan 7d): Dashboard (the grid), Katalog (every widget as a
/// card with its picture, description, stars and Add) and Statystyki (the weekly summary). One
/// grouped switch in the header, like the SegControl of the Settings page; the active option
/// moves. Edit mode belongs to the Dashboard view only.
/// </summary>
internal sealed partial class DashCanvas
{
    internal enum View { Dashboard, Catalog, Stats }
    private View _view;

    internal View CurrentView => _view;

    internal void SwitchView(View v)
    {
        if (v == _view) return;
        if (v != View.Dashboard && _edit) EditMode = false;
        _view = v;
        _hot = new Hot(Zone.None, -1, null);
        if (v == View.Stats && Summary != null) StartStatsEntrance();   // before the static render, which leaves the animated parts out
        Replace();
        ContentChanged?.Invoke();
    }

    // ---- the header switch -----------------------------------------------------------------
    private Font SwitchFont() => _ctx.Font(11f, FontStyle.Bold);
    private int SwitchH => Math.Max(S(38), SwitchFont().Height + S(16));

    /// <summary>Dashboard | Katalog | Statystyki at the left of the header; returns the group's right edge.</summary>
    private int PaintViewSwitch(Graphics g, int left, int top, int bandH)
    {
        var f = SwitchFont();
        string[] labels = { Lang.T("tab_dashboard"), Lang.T("dash_view_catalog"), Lang.T("dash_view_stats") };
        int h = SwitchH, padX = S(18), inset = S(3);
        var w = labels.Select(l => TextRenderer.MeasureText(l, f, Size.Empty, TextFormatFlags.NoPadding).Width + padX * 2).ToArray();
        var group = new Rectangle(left, top + (bandH - h) / 2, w.Sum() + inset * 2, h);
        using (var path = Theme.RoundRect(group, Sf(10)))
        {
            using var b = new SolidBrush(Theme.Card);
            g.FillPath(b, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
        int x = group.Left + inset;
        for (int i = 0; i < labels.Length; i++)
        {
            var r = new Rectangle(x, group.Top + inset, w[i], h - inset * 2);
            bool on = i == (int)_view;
            if (on)
            {
                using var path = Theme.RoundRect(r, Sf(8));
                using var b = new SolidBrush(Theme.AccentFill);
                g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, labels[i], f, r, on ? Color.White : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            _zones.Add((r, new Hot(Zone.View, i, null)));
            x += w[i];
        }
        return group.Right;
    }

    /// <summary>The two sort pills, right-aligned at <paramref name="right"/>, vertically centred in <paramref name="lineH"/>; returns the left edge.</summary>
    private int PaintSortPills(Graphics g, int right, int top, int lineH)
    {
        string[] labels = { Lang.T("dash_sort_popular"), Lang.T("dash_sort_az") };
        for (int i = 1; i >= 0; i--)
        {
            int pw = TextRenderer.MeasureText(labels[i], CatSmallF, Size.Empty, TextFormatFlags.NoPadding).Width + S(22);
            var pr = new Rectangle(right - pw, top + (lineH - SortPillH) / 2, pw, SortPillH);
            bool on = (i == 0) == _sortPopular;
            using (var path = Theme.RoundRect(pr, SortPillH / 2f))   // already device pixels: a full pill, no second scaling
            {
                using var b = new SolidBrush(on ? Theme.AccentSoft : Theme.Surface);
                g.FillPath(b, path);
                using var pen = new Pen(on ? Theme.Accent : Theme.Border);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, labels[i], CatSmallF, pr, on ? Theme.Accent : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            _zones.Add((pr, new Hot(Zone.Sort, i, null)));
            right = pr.Left - S(8);
        }
        return right + S(8);
    }

    // ---- Katalog: every widget as a card --------------------------------------------------
    private int CatCols(int width) => Math.Clamp((width - Pad * 2 + Gap) / (S(320) + Gap), 1, 4);
    private int CatCardW(int width) { int c = CatCols(width); return (width - Pad * 2 - Gap * (c - 1)) / c; }
    private int CatPreviewH => S(170);
    private int CatBtnH => Math.Max(S(26), _ctx.Font(9f, FontStyle.Bold).Height + S(8));

    /// <summary>Uniform card height: the picture box, the name, two description lines, the stars line with the button.</summary>
    private int CatCardH() => S(16) + CatPreviewH + S(12) + CatNameF.Height + S(2) + CatDescF.Height * 2 + S(10) + Math.Max(StarSize, CatBtnH) + S(16);

    private int CatalogViewHeight(int width)
    {
        int cols = CatCols(width), rows = (Listed.Count() + cols - 1) / cols;
        return rows * CatCardH() + (rows - 1) * Gap;
    }

    /// <summary>
    /// The widget's picture at its natural cell size (the placed instance when it is on the
    /// board, so the live values show; a fresh one otherwise), to be drawn scaled down.
    /// </summary>
    private Bitmap RenderPreview(DashWidgetDef def)
    {
        int k = _widgets.FindIndex(w => w.TypeId == def.TypeId);
        var w = k >= 0 ? _widgets[k] : def.New();
        var size = def.Sizes.Length > 0 ? def.Sizes[0] : new Size(1, 1);
        int cell = S(150), gap = Gap;
        int pw = size.Width * cell + (size.Width - 1) * gap, ph = size.Height * cell + (size.Height - 1) * gap;
        var bmp = new Bitmap(pw, ph);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        var r = new Rectangle(0, 0, pw, ph);
        try { w.PaintStatic(g, r, _ctx); w.PaintLive(g, r, _ctx); } catch { }
        return bmp;
    }

    private void PaintCatalogView(Graphics g)
    {
        var sum = Summary;
        int cols = CatCols(Width), cw = CatCardW(Width), ch = CatCardH(), pad = S(16);
        var order = CatalogOrder(sum);
        var present = new HashSet<string>(_items.Select(i => i.Type));
        var popular = sum == null ? new HashSet<string>() : new HashSet<string>(sum.Top.Take(3));
        string badge = Lang.Upper(Lang.T("dash_sort_popular"));
        var nameF = CatNameF;
        var descF = CatDescF;
        var btnF = _ctx.Font(9f, FontStyle.Bold);
        var ratings = _d.Settings.WidgetRatings;
        _starRows.Clear();
        for (int n = 0; n < order.Count; n++)
        {
            var def = order[n];
            var card = new Rectangle(Pad + (n % cols) * (cw + Gap), GridTop() + (n / cols) * (ch + Gap), cw, ch);
            Ui.FillCard(g, card);
            int x = card.Left + pad, w = cw - pad * 2, y = card.Top + pad;

            // the picture, scaled to fit its box and never enlarged
            var box = new Rectangle(x, y, w, CatPreviewH);
            using (var path = Theme.RoundRect(box, Sf(10)))
            {
                using var b = new SolidBrush(Theme.Surface);
                g.FillPath(b, path);
            }
            using (var pv = RenderPreview(def))
            {
                float sc = Math.Min(1f, Math.Min((box.Width - S(16)) / (float)pv.Width, (box.Height - S(16)) / (float)pv.Height));
                int dw = Math.Max(1, (int)(pv.Width * sc)), dh = Math.Max(1, (int)(pv.Height * sc));
                var old = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(pv, new Rectangle(box.Left + (box.Width - dw) / 2, box.Top + (box.Height - dh) / 2, dw, dh));
                g.InterpolationMode = old;
            }
            y = box.Bottom + S(12);

            bool available = def.Available(_d), onBoard = present.Contains(def.TypeId);
            var st = sum != null && sum.Widgets.TryGetValue(def.TypeId, out var ws) ? ws : null;

            // name and the POPULAR badge
            string name = Lang.T(def.NameKey);
            int nameW = Math.Min(w, TextRenderer.MeasureText(name, nameF, Size.Empty, TextFormatFlags.NoPadding).Width);
            TextRenderer.DrawText(g, name, nameF, new Rectangle(x, y, w, nameF.Height), available ? Theme.Text : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            if (popular.Contains(def.TypeId))
            {
                int bwd = TextRenderer.MeasureText(badge, CatSmallF, Size.Empty, TextFormatFlags.NoPadding).Width + S(10), bht = CatSmallF.Height + S(4);
                if (nameW + S(8) + bwd <= w)
                {
                    var bdg = new Rectangle(x + nameW + S(8), y + (nameF.Height - bht) / 2, bwd, bht);
                    using var path = Theme.RoundRect(bdg, bht / 2f);
                    using var b = new SolidBrush(Theme.AccentSoft);
                    g.FillPath(b, path);
                    TextRenderer.DrawText(g, badge, CatSmallF, bdg, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
            y += nameF.Height + S(2);

            // description, two lines
            string desc = CatDesc(def, available, onBoard);
            TextRenderer.DrawText(g, desc, descF, new Rectangle(x, y, w, descF.Height * 2), onBoard && available ? Theme.Accent : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            y += descF.Height * 2 + S(10);

            // the stars line with the community text; the Add button at the right
            int lineH = Math.Max(StarSize, CatBtnH);
            string btn = Lang.T("dash_add_short");
            int bw = TextRenderer.MeasureText(btn, btnF, Size.Empty, TextFormatFlags.NoPadding).Width + S(20);
            var br = new Rectangle(card.Right - pad - bw, y + (lineH - CatBtnH) / 2, bw, CatBtnH);
            bool showBtn = available && !onBoard;
            var starRow = new Rectangle(x, y + (lineH - StarSize) / 2, StarSize * 5, StarSize);
            _starRows[def.TypeId] = starRow;
            int own = ratings.TryGetValue(def.TypeId, out int r0) ? r0 : 0;
            int avg = st?.Rating is { } a ? (int)Math.Round(a) : 0;
            PaintStars(g, starRow, own > 0 ? own : avg, own > 0 ? Theme.Accent : Theme.Muted, Theme.Faint, clear: false);
            for (int k = 1; k <= 5; k++) _zones.Add((DashRatings.StarRect(starRow.Location, StarSize, k), new Hot(Zone.Star, k, def.TypeId)));
            if (st != null)
            {
                string votes = DashRatings.Votes(st);
                string share = string.Format(Lang.T("dash_share_fmt"), (int)Math.Round(st.Share * 100));
                int tx = starRow.Right + S(8), tw = (showBtn ? br.Left - S(10) : card.Right - pad) - tx;
                string tail = votes.Length > 0 ? votes + " · " + share : share;
                if (TextRenderer.MeasureText(tail, descF, Size.Empty, TextFormatFlags.NoPadding).Width > tw) tail = share;
                if (TextRenderer.MeasureText(tail, descF, Size.Empty, TextFormatFlags.NoPadding).Width > tw) tail = (int)Math.Round(st.Share * 100) + " %";
                TextRenderer.DrawText(g, tail, descF, new Rectangle(tx, y + (lineH - descF.Height) / 2, Math.Max(0, tw), descF.Height), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }
            if (showBtn)
            {
                using (var path = Theme.RoundRect(br, Sf(6)))
                {
                    using var b = new SolidBrush(Theme.AccentFill);
                    g.FillPath(b, path);
                }
                TextRenderer.DrawText(g, btn, btnF, br, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                _zones.Add((br, new Hot(Zone.CatalogAdd, -1, def.TypeId)));
            }
            // the whole card opens the widget card (added last: the stars and the button win where they overlap)
            _zones.Add((card, new Hot(Zone.Info, -1, def.TypeId)));
        }
    }
}
