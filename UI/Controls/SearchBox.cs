using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// The app-drawn search field (a magnifier, a rounded box, an X that clears): used by the Models tab
/// and the change history. Filters live through <see cref="Box"/>.TextChanged.
/// </summary>
internal sealed class SearchBox : Control
{
    public readonly TextBox Box = new() { BorderStyle = BorderStyle.None, Font = Dpi.Font("Segoe UI", 10.5f) };
    private static readonly Font Glyph = Dpi.Font("Segoe MDL2 Assets", 10.5f);
    private static readonly Font ClearGlyph = Dpi.Font("Segoe MDL2 Assets", 8.5f);

    public SearchBox()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Cursor = Cursors.IBeam;
        Controls.Add(Box);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Box.TextChanged += (_, _) => { PerformLayout(); Invalidate(); };   // the X comes and goes
        ApplyTheme();
    }

    private int S(int v) => (int)Math.Ceiling(v * Dpi.Of(this) / 96f);
    public int PreferredHeight => Box.PreferredHeight + S(16);

    public void ApplyTheme() { Box.BackColor = Theme.Card; Box.ForeColor = Theme.Text; BackColor = Theme.Surface; Invalidate(); }

    private bool HasText => Box.TextLength > 0;
    private Rectangle ClearRect => new(Width - S(30), (Height - S(22)) / 2, S(22), S(22));
    private bool _overClear;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool over = HasText && ClearRect.Contains(e.Location);
        Cursor = over ? Cursors.Hand : Cursors.IBeam;
        if (over != _overClear) { _overClear = over; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_overClear) { _overClear = false; Invalidate(); } }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (HasText && ClearRect.Contains(e.Location)) Box.Clear();   // TextChanged re-filters
        Box.Focus();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int left = S(34), right = HasText ? S(36) : S(12);
        Box.SetBounds(left, (Height - Box.Height) / 2, Math.Max(10, Width - left - right), Box.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Surface);
        bool focus = Box.Focused;
        using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 7))
        {
            using var b = new SolidBrush(Theme.Card); g.FillPath(b, path);
            using var pen = new Pen(focus ? Theme.Accent : Theme.Border, focus ? 1.5f : 1f); g.DrawPath(pen, path);
        }
        Ui.CenterGlyph(g, "\uE721", Glyph, focus ? Theme.Accent : Theme.Muted, new RectangleF(S(8), 0, S(20), Height));
        if (HasText)
        {
            var cr = ClearRect;
            if (_overClear)
            {
                using var b = new SolidBrush(Color.FromArgb(Theme.Dark ? 30 : 20, Theme.Text));
                g.FillEllipse(b, cr);
            }
            Ui.CenterGlyph(g, "\uE711", ClearGlyph, _overClear ? Theme.Text : Theme.Muted, cr);
        }
    }
}
