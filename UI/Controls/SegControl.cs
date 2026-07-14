using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

public sealed class SegControl : Control
{
    private readonly string[] _items;
    private int _sel;
    public event Action<int>? SelectedChanged;
    public int Selected { get => _sel; set { _sel = value; Invalidate(); } }

    public SegControl(string[] items, int sel) { _items = items; _sel = sel; DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Hand; }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int seg = Math.Clamp(e.X * _items.Length / Math.Max(1, Width), 0, _items.Length - 1);
        if (seg != _sel) { _sel = seg; Invalidate(); SelectedChanged?.Invoke(_sel); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = Theme.RoundRect(outer, 8))
        {
            using var b = new SolidBrush(Theme.Card); g.FillPath(b, path);
            using var pen = new Pen(Theme.Border); g.DrawPath(pen, path);
        }
        float seg = (float)Width / _items.Length;
        for (int i = 0; i < _items.Length; i++)
        {
            var r = new RectangleF(i * seg, 0, seg, Height);
            if (i == _sel)
            {
                using var path = Theme.RoundRect(new RectangleF(r.X + 2, 2, r.Width - 4, Height - 4), 7);
                using var b = new SolidBrush(Theme.AccentFill); g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, _items[i], new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Rectangle.Round(r), i == _sel ? Color.White : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
