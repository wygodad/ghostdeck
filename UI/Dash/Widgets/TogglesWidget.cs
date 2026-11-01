namespace GhostDeck;

/// <summary>
/// Quick toggles: Fan Boost, the gaming overlay, the webcam, the microphone, the touchpad and
/// the Windows-key lock as drawn switches, two columns (2x1) or four (4x1). Switches whose
/// feature this machine does not have are left out, as the Scenarios bricks are. A click flips
/// the switch through the MainDeps setter, with the same gates as everywhere else.
/// </summary>
internal sealed class TogglesWidget : DashWidget
{
    private sealed record Item(string LabelKey, Func<DashSample, bool> Get, Action<DashContext, bool> Set, Func<DashSample, bool> Available, bool NeedsWritable);

    private static readonly Item[] Items =
    {
        new("cooler_boost_short", s => s.CoolerBoost,  (c, v) => c.D.SetCoolerBoost(v), _ => true,          true),
        new("overlay_title",      s => s.Overlay,      (c, v) => c.D.SetOverlay(v),     _ => true,          false),
        new("webcam_title",       s => s.Webcam == 1,  (c, v) => c.D.SetWebcam(v),      s => s.Webcam >= 0, true),
        new("mic_title",          s => s.Mic == 1,     (c, v) => c.D.SetMic(v),         s => s.Mic >= 0,    false),
        new("tp_title",           s => s.Touchpad == 1,(c, v) => c.D.SetTouchpad(v),    s => s.Touchpad >= 0, false),
        new("winlock_title",      s => s.WinLock,      (c, v) => c.D.SetWinLock(v),     _ => true,          false),
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
        var f = c.Font(10f, FontStyle.Bold);
        int swH = Math.Max(c.S(22), f.Height + c.S(4)), swW = swH * 52 / 28;
        bool writable = c.Writable;
        _cells.Clear();
        for (int i = 0; i < items.Count && i < cols * rows; i++)
        {
            var cell = new Rectangle(inner.Left + (i % cols) * (cellW + gap), inner.Top + (i / cols) * (cellH + gap), cellW, cellH);
            using (var path = Theme.RoundRect(cell, c.Sf(8)))
            {
                using var b = new SolidBrush(Theme.Surface);
                g.FillPath(b, path);
                using var pen = new Pen(Theme.Border);
                g.DrawPath(pen, path);
            }
            var item = items[i];
            bool enabled = !item.NeedsWritable || writable;
            var sw = new Rectangle(cell.Right - swW - c.S(10), cell.Top + (cell.Height - swH) / 2, swW, swH);
            DashPaint.Switch(g, sw, enabled && item.Get(s), enabled);
            var tr = new Rectangle(cell.Left + c.S(10), cell.Top, Math.Max(10, sw.Left - cell.Left - c.S(16)), cell.Height);
            TextRenderer.DrawText(g, Lang.T(item.LabelKey), f, tr, enabled ? Theme.Text : Theme.Muted,
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
