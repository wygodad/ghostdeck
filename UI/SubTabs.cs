using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// Reusable segmented sub-tab bar (a themed pill). Used to split a page into a few
/// content sub-pages (Report: Profile/Curve; Status: Charts/EC bytes/Change log)
/// without adding top-level tabs. Self-sizing width; raises <see cref="Changed"/> on click.
/// </summary>
public sealed class SubTabs : Control
{
    private const int SegPadX = 18, Inset = 3;
    private static readonly Font SegFont = new("Segoe UI", 10.5f, FontStyle.Bold);

    private readonly string[] _labels;
    private int _active;
    private int _hover = -1;

    public event Action<int>? Changed;
    public int Active => _active;

    public SubTabs(params string[] labels)
    {
        _labels = labels;
        DoubleBuffered = true;
        ResizeRedraw = true;
        Cursor = Cursors.Hand;
        Height = 40;
        Width = Measure();
    }

    /// <summary>Total width the segments need (parent positions us with this width).</summary>
    public int PreferredWidth => Measure();

    private int Measure()
    {
        int w = Inset * 2;
        foreach (var l in _labels) w += TextRenderer.MeasureText(l, SegFont).Width + SegPadX * 2;
        return w;
    }

    /// <summary>Programmatic selection. Set <paramref name="raise"/> to fire <see cref="Changed"/>.</summary>
    public void SetActive(int i, bool raise = false)
    {
        i = Math.Clamp(i, 0, _labels.Length - 1);
        if (i == _active) { if (raise) Changed?.Invoke(i); return; }
        _active = i;
        Invalidate();
        if (raise) Changed?.Invoke(i);
    }

    private RectangleF[] Segments()
    {
        var rects = new RectangleF[_labels.Length];
        float x = Inset, y = Inset, h = Height - Inset * 2;
        for (int i = 0; i < _labels.Length; i++)
        {
            float w = TextRenderer.MeasureText(_labels[i], SegFont).Width + SegPadX * 2;
            rects[i] = new RectangleF(x, y, w, h);
            x += w;
        }
        return rects;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var segs = Segments();
        int h = -1;
        for (int i = 0; i < segs.Length; i++) if (segs[i].Contains(e.Location)) { h = i; break; }
        if (h != _hover) { _hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_hover != -1) { _hover = -1; Invalidate(); } }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var segs = Segments();
        for (int i = 0; i < segs.Length; i++)
            if (segs[i].Contains(e.Location)) { SetActive(i, raise: true); return; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Surface);

        // outer container — softly rounded (rectangular-ish, like the theme toggle), not a full pill
        var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = Theme.RoundRect(outer, 9))
        {
            using var b = new SolidBrush(Theme.Page);
            g.FillPath(b, path);
            using var p = new Pen(Theme.Border);
            g.DrawPath(p, path);
        }

        var segs = Segments();
        for (int i = 0; i < segs.Length; i++)
        {
            bool active = i == _active;
            var r = segs[i];
            if (active)
            {
                using var path = Theme.RoundRect(new RectangleF(r.X, r.Y, r.Width, r.Height), 7);
                using var b = new SolidBrush(Theme.Surface);
                g.FillPath(b, path);
                using var p = new Pen(Theme.Accent, 1.4f);
                g.DrawPath(p, path);
            }
            else if (i == _hover)
            {
                using var path = Theme.RoundRect(new RectangleF(r.X, r.Y, r.Width, r.Height), 7);
                using var b = new SolidBrush(Theme.Card);
                g.FillPath(b, path);
            }
            var col = active ? Theme.Accent : (i == _hover ? Theme.Text : Theme.Muted);
            TextRenderer.DrawText(g, _labels[i], SegFont, Rectangle.Round(r), col,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
