namespace GhostDeck;

/// <summary>
/// Quick toggles: Fan Boost, the gaming overlay, the webcam, the microphone, the touchpad and
/// the Windows-key lock as drawn switches, two columns (2x1) or four (4x1). Switches whose
/// feature this machine does not have are left out, as the Scenarios bricks are. A click flips
/// the switch through the MainDeps setter, with the same gates as everywhere else.
///
/// Settings (1.45): which switches the widget shows and the colour of a switched-on one. With
/// copies of the widget that makes small ones: a single switch fits a single cell, where its
/// icon, name and switch stand one under another. With nothing picked the widget lays out
/// exactly as in 1.44.
/// </summary>
internal sealed class TogglesWidget : DashWidget
{
    private const string KShow = "show", KColor = "color";

    private sealed record Item(string Id, string LabelKey, string Glyph, Func<DashSample, bool> Get, Action<DashContext, bool> Set, Func<DashSample, bool> Available, bool NeedsWritable);

    // the glyphs are the Scenarios page's feature bricks (the same feature looks the same everywhere);
    // the ids go into the layout's settings and never change
    private static readonly Item[] Items =
    {
        new("boost",    "cooler_boost_short", "❄",            s => s.CoolerBoost,  (c, v) => c.D.SetCoolerBoost(v), _ => true,          true),
        new("overlay",  "overlay_title",      "▦",            s => s.Overlay,      (c, v) => c.D.SetOverlay(v),     _ => true,          false),
        new("webcam",   "webcam_title",       "◉",            s => s.Webcam == 1,  (c, v) => c.D.SetWebcam(v),      s => s.Webcam >= 0, true),
        new("mic",      "mic_title",          "\U0001F3A4",   s => s.Mic == 1,     (c, v) => c.D.SetMic(v),         s => s.Mic >= 0,    false),
        new("touchpad", "tp_title",           "▭",            s => s.Touchpad == 1,(c, v) => c.D.SetTouchpad(v),    s => s.Touchpad >= 0, false),
        new("winlock",  "winlock_title",      "⊞",            s => s.WinLock,      (c, v) => c.D.SetWinLock(v),     _ => true,          false),
    };

    private readonly IReadOnlyList<Item> _picked;   // the switches to show, in the order above
    private readonly bool _all;                     // nothing picked: the layout of 1.44
    private string _painted = "";
    private readonly List<(Rectangle r, Item item)> _cells = new();

    public TogglesWidget(DashWidgetDef def, DashOptions opt) : base(def, opt)
    {
        _picked = Picked(KShow, Items, i => i.Id);
        _all = opt.List(KShow) == null;
        if (!_all && _picked.Count <= 1) Sizes = Sz((1, 1), (2, 1));
    }

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c) => Frame(g, r);

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        var s = c.Sample;
        var items = _picked.Where(i => i.Available(s)).ToList();
        _cells.Clear();
        _painted = Signature(c);
        if (items.Count == 0) { DashPaint.Note(g, Inner(r, c), c, Lang.T("dash_sel_none")); return; }
        // a lone switch gets more of the card: it is the whole widget
        var inner = items.Count == 1 ? Rectangle.Inflate(r, -c.S(8), -c.S(8)) : Inner(r, c);
        bool wide = r.Width > r.Height * 2.5f;
        int n = items.Count, cols, rows;
        if (_all) { cols = wide ? 4 : 2; rows = 2; }                                             // as in 1.44, whatever this machine has
        else if (wide) { cols = n <= 2 ? n : n <= 4 ? 2 : 4; rows = n <= 2 ? 1 : 2; }            // picked: the cells share the card
        else { cols = n <= 2 ? 1 : 2; rows = n == 1 ? 1 : 2; }
        int gap = c.S(8);
        int cellW = (inner.Width - gap * (cols - 1)) / cols, cellH = (inner.Height - gap * (rows - 1)) / rows;
        // the Scenarios brick look: an outlined icon box in the accent, the label beside it, the switch
        // at the right; everything keeps a clear margin from the cell's edges. A narrow cell (four
        // across) takes the smaller font and box so "Nakładka do gier" still has room to wrap. A cell
        // that is narrow and tall (a single switch in a single grid cell) stacks the three instead.
        bool narrow = cellW < c.S(230), stacked = cellH >= c.S(96) && cellW < c.S(200);
        var f = c.Font(narrow ? 9.5f : 10f, FontStyle.Bold);
        int swH = Math.Max(c.S(22), f.Height + c.S(4)), swW = swH * 52 / 28;
        int pad = c.S(narrow ? 11 : 14), box = Math.Min(c.S(narrow ? 26 : 30), cellH - c.S(12)), boxGap = c.S(narrow ? 8 : 10);
        using var gf = Dpi.Font("Segoe UI Symbol", narrow ? 10f : 11f);
        bool writable = c.Writable;
        string pick = Opt.Choice(KColor, "auto", DashPaint.PaletteChoices);
        var onColor = DashPaint.Pick(pick, Theme.Accent);
        Color? track = pick == "auto" ? null : onColor;   // the switch keeps the app's own blue unless a colour was picked
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        // A label is never cut. Where a word of some label does not fit beside the icon box even in
        // the small font (a long language in a narrow cell), every cell of the widget gives the
        // icon's room to its label; where it does not fit even then, the cells keep the icon and the
        // switch and show no label.
        var small = c.Font(8.5f, FontStyle.Bold);
        int shown = Math.Min(items.Count, cols * rows);
        bool WordsFit(int room) => room >= c.S(40) && items.Take(shown).All(it =>
            Lang.T(it.LabelKey).Split(' ').All(w => TextRenderer.MeasureText(w, small, Size.Empty, TextFormatFlags.NoPadding).Width <= room));
        int roomIcon = cellW - pad - box - boxGap - c.S(8) - swW - pad, roomBare = cellW - pad - c.S(8) - swW - pad;
        bool icons = stacked || WordsFit(roomIcon) || !WordsFit(roomBare);
        bool labels = stacked || WordsFit(roomIcon) || WordsFit(roomBare);
        for (int i = 0; i < shown; i++)
        {
            var cell = new Rectangle(inner.Left + (i % cols) * (cellW + gap), inner.Top + (i / cols) * (cellH + gap), cellW, cellH);
            var item = items[i];
            bool enabled = !item.NeedsWritable || writable;
            bool on = enabled && item.Get(s);
            using (var path = Theme.RoundRect(cell, c.Sf(10)))
            {
                using var b = new SolidBrush(Theme.Surface);
                g.FillPath(b, path);
                using var pen = new Pen(on ? Color.FromArgb(120, onColor) : Theme.Border);   // a switched-on cell shows a faint edge in its colour
                g.DrawPath(pen, path);
            }
            var iconColor = !enabled ? Theme.Faint : on ? onColor : Theme.Muted;
            string label = Lang.T(item.LabelKey);
            _cells.Add((cell, item));
            if (stacked)
            {
                // icon, name (two lines at most), switch: one under another, centred as a block
                var lf2 = c.Font(9.5f, FontStyle.Bold);
                int tw = cell.Width - c.S(8) * 2;
                int lh = Math.Min(lf2.Height * 2, TextRenderer.MeasureText(label, lf2, new Size(tw, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height);
                int g1 = c.S(7), g2 = c.S(9);
                int top = cell.Top + Math.Max(c.S(6), (cell.Height - (box + g1 + lh + g2 + swH)) / 2);
                var sb = new RectangleF(cell.Left + (cell.Width - box) / 2f, top, box, box);
                using (var ip = Theme.RoundRect(new RectangleF(sb.X + 0.5f, sb.Y + 0.5f, box - 1, box - 1), c.Sf(6)))
                using (var pen = new Pen(iconColor, c.Sf(1.4f)))
                    g.DrawPath(pen, ip);
                Ui.CenterGlyphInk(g, item.Glyph, gf, iconColor, sb);
                TextRenderer.DrawText(g, label, lf2, new Rectangle(cell.Left + c.S(8), top + box + g1, tw, lh), enabled ? Theme.Text : Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                DashPaint.Switch(g, new Rectangle(cell.Left + (cell.Width - swW) / 2, top + box + g1 + lh + g2, swW, swH), on, enabled, track);
                continue;
            }
            var ib = new RectangleF(cell.Left + pad, cell.Top + (cell.Height - box) / 2f, box, box);
            if (icons)
            {
                using (var ip = Theme.RoundRect(new RectangleF(ib.X + 0.5f, ib.Y + 0.5f, box - 1, box - 1), c.Sf(6)))
                using (var pen = new Pen(iconColor, c.Sf(1.4f)))
                    g.DrawPath(pen, ip);
                Ui.CenterGlyphInk(g, item.Glyph, gf, iconColor, ib);   // on the ink, so ❄ and ▦ sit in the middle of the box
            }
            var sw = new Rectangle(cell.Right - swW - pad, cell.Top + (cell.Height - swH) / 2, swW, swH);
            DashPaint.Switch(g, sw, on, enabled, track);
            int tx = icons ? (int)ib.Right + boxGap : cell.Left + pad;
            var tr = new Rectangle(tx, cell.Top, Math.Max(10, sw.Left - tx - c.S(8)), cell.Height);
            // a single word wider than the column ("Mikrofon" in a 2x1 cell) takes the smaller font instead of a cut
            var lf = f;
            if (label.Split(' ').Any(w => TextRenderer.MeasureText(w, f, Size.Empty, TextFormatFlags.NoPadding).Width > tr.Width)) lf = small;
            if (labels)
                TextRenderer.DrawText(g, label, lf, tr, enabled ? Theme.Text : Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }

    private string Signature(DashContext c)
    {
        var s = c.Sample;
        var sb = new System.Text.StringBuilder();
        sb.Append(c.Writable ? 'W' : 'r');
        foreach (var i in _picked) sb.Append(i.Available(s) ? (i.Get(s) ? '1' : '0') : '-');
        return sb.ToString();
    }

    public override bool LiveChanged(DashContext c) => Signature(c) != _painted;

    public override void Click(Point p, Rectangle r, DashContext c)
    {
        foreach (var (cr, item) in _cells)
            if (cr.Contains(p))
            {
                if (item.NeedsWritable && !c.Writable) return;
                item.Set(c, !item.Get(c.Sample));
                return;
            }
    }

    public override IReadOnlyList<DashOptionDef> OptionDefs(DashContext c)
    {
        // only the switches this machine has are offered, as only they are ever shown
        var have = Items.Where(i => i.Available(c.Sample)).ToList();
        var list = new List<DashOptionDef>();
        if (have.Count > 1) list.Add(new(KShow, DashOptKind.Checks, Lang.T("dash_opt_show")) { Items = have.Select(i => (i.Id, Lang.T(i.LabelKey))).ToArray() });
        list.Add(new(KColor, DashOptKind.Color, Lang.T("dash_opt_on_color")) { Default = "auto", Auto = _ => Theme.Accent });
        return list;
    }
}
