namespace GhostDeck;

/// <summary>
/// Quick toggles: Fan Boost, the gaming overlay, the webcam, the microphone, the touchpad and
/// the Windows-key lock as drawn switches, two columns (2x1) or four (4x1). Switches whose
/// feature this machine does not have are left out, as the Scenarios bricks are. A click flips
/// the switch through the MainDeps setter, with the same gates as everywhere else.
/// </summary>
internal sealed class TogglesWidget : DashWidget
{
    private sealed record Item(string LabelKey, string Glyph, Func<DashSample, bool> Get, Action<DashContext, bool> Set, Func<DashSample, bool> Available, bool NeedsWritable);

    // the glyphs are the Scenarios page's feature bricks (the same feature looks the same everywhere)
    private static readonly Item[] Items =
    {
        new("cooler_boost_short", "❄",            s => s.CoolerBoost,  (c, v) => c.D.SetCoolerBoost(v), _ => true,          true),
        new("overlay_title",      "▦",            s => s.Overlay,      (c, v) => c.D.SetOverlay(v),     _ => true,          false),
        new("webcam_title",       "◉",            s => s.Webcam == 1,  (c, v) => c.D.SetWebcam(v),      s => s.Webcam >= 0, true),
        new("mic_title",          "\U0001F3A4",   s => s.Mic == 1,     (c, v) => c.D.SetMic(v),         s => s.Mic >= 0,    false),
        new("tp_title",           "▭",            s => s.Touchpad == 1,(c, v) => c.D.SetTouchpad(v),    s => s.Touchpad >= 0, false),
        new("winlock_title",      "⊞",            s => s.WinLock,      (c, v) => c.D.SetWinLock(v),     _ => true,          false),
    };

    private string _painted = "";
    private readonly List<(Rectangle r, Item item)> _cells = new();

    public TogglesWidget(DashWidgetDef def) : base(def.TypeId, def.NameKey, def.Sizes) { }

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c) => Frame(g, r);

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        var inner = Inner(r, c);
        var s = c.Sample;
        var items = Items.Where(i => i.Available(s)).ToList();
        int cols = r.Width > r.Height * 2.5f ? 4 : 2, rows = 2;
        int gap = c.S(8);
        int cellW = (inner.Width - gap * (cols - 1)) / cols, cellH = (inner.Height - gap * (rows - 1)) / rows;
        // the Scenarios brick look: an outlined icon box in the accent, the label beside it, the switch
        // at the right; everything keeps a clear margin from the cell's edges. A narrow cell (four
        // across) takes the smaller font and box so "Nakładka do gier" still has room to wrap.
        bool narrow = cellW < c.S(230);
        var f = c.Font(narrow ? 9.5f : 10f, FontStyle.Bold);
        int swH = Math.Max(c.S(22), f.Height + c.S(4)), swW = swH * 52 / 28;
        int pad = c.S(narrow ? 11 : 14), box = Math.Min(c.S(narrow ? 26 : 30), cellH - c.S(12)), boxGap = c.S(narrow ? 8 : 10);
        using var gf = Dpi.Font("Segoe UI Symbol", narrow ? 10f : 11f);
        bool writable = c.Writable;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        _cells.Clear();
        for (int i = 0; i < items.Count && i < cols * rows; i++)
        {
            var cell = new Rectangle(inner.Left + (i % cols) * (cellW + gap), inner.Top + (i / cols) * (cellH + gap), cellW, cellH);
            var item = items[i];
            bool enabled = !item.NeedsWritable || writable;
            bool on = enabled && item.Get(s);
            using (var path = Theme.RoundRect(cell, c.Sf(10)))
            {
                using var b = new SolidBrush(Theme.Surface);
                g.FillPath(b, path);
                using var pen = new Pen(on ? Color.FromArgb(120, Theme.Accent) : Theme.Border);   // a switched-on cell shows a faint accent edge
                g.DrawPath(pen, path);
            }
            var ib = new RectangleF(cell.Left + pad, cell.Top + (cell.Height - box) / 2f, box, box);
            var iconColor = !enabled ? Theme.Faint : on ? Theme.Accent : Theme.Muted;
            using (var ip = Theme.RoundRect(new RectangleF(ib.X + 0.5f, ib.Y + 0.5f, box - 1, box - 1), c.Sf(6)))
            using (var pen = new Pen(iconColor, c.Sf(1.4f)))
                g.DrawPath(pen, ip);
            Ui.CenterGlyph(g, item.Glyph, gf, iconColor, ib);
            var sw = new Rectangle(cell.Right - swW - pad, cell.Top + (cell.Height - swH) / 2, swW, swH);
            DashPaint.Switch(g, sw, on, enabled);
            int tx = (int)ib.Right + boxGap;
            var tr = new Rectangle(tx, cell.Top, Math.Max(10, sw.Left - tx - c.S(8)), cell.Height);
            // a single word wider than the column ("Mikrofon" in a 2x1 cell) takes the smaller font instead of a cut
            string label = Lang.T(item.LabelKey);
            var lf = f;
            if (label.Split(' ').Any(w => TextRenderer.MeasureText(w, f, Size.Empty, TextFormatFlags.NoPadding).Width > tr.Width)) lf = c.Font(8.5f, FontStyle.Bold);
            TextRenderer.DrawText(g, label, lf, tr, enabled ? Theme.Text : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            _cells.Add((cell, item));
        }
        _painted = Signature(c);
    }

    private static string Signature(DashContext c)
    {
        var s = c.Sample;
        var sb = new System.Text.StringBuilder();
        sb.Append(c.Writable ? 'W' : 'r');
        foreach (var i in Items) sb.Append(i.Available(s) ? (i.Get(s) ? '1' : '0') : '-');
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
}
