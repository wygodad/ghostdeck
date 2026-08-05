using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// Reusable segmented sub-tab bar (a themed pill). Used to split a page into a few
/// content sub-pages (Report: Profile/Curve; Status: Charts/EC bytes/Change log)
/// without adding top-level tabs. Self-sizing width; raises <see cref="Changed"/> on click.
/// </summary>
public sealed class SubTabs : Control
{
    private const int SegPadX = 18, Inset = 3, GlyphGap = 7;
    private static readonly Font SegFont = new("Segoe UI", 10.5f, FontStyle.Bold);
    // Same icon language as the main tab strip: PUA glyphs come from Segoe MDL2 Assets.
    private static readonly Font GlyphFont = new("Segoe MDL2 Assets", 11f);

    private readonly string[] _labels;
    private readonly string[]? _glyphs;   // optional, one per label ("" = none for that segment)
    private int _active;
    private int _hover = -1;
    private bool _compact;                // narrow window: icons only, except active + hovered
    private int _avail = int.MaxValue;    // width the page can give us (set by FitTo)

    public event Action<int>? Changed;
    public int Active => _active;

    public SubTabs(params string[] labels) : this(labels, null) { }

    public SubTabs(string[] labels, string[]? glyphs)
    {
        _labels = labels;
        _glyphs = glyphs;
        DoubleBuffered = true;
        ResizeRedraw = true;
        Cursor = Cursors.Hand;
        Height = 40;
        Width = Measure();
    }

    /// <summary>Total width the segments need (parent positions us with this width).</summary>
    public int PreferredWidth => Measure();

    /// <summary>
    /// Fit the strip into the width the page can give it. The full strip does not fit at the
    /// minimum window size in ANY language (measured: en 842, pl 891, ru 923, de 927, fr 953
    /// against 867 px of client), which pushed a horizontal scrollbar onto the whole page.
    /// When it does not fit we drop to icons only - except the ACTIVE segment, which always
    /// keeps its label so you can see where you are, and the one under the cursor, which
    /// expands to icon + label in place and collapses again as soon as you leave it.
    /// Returns the width to position us with.
    /// </summary>
    public int FitTo(int available)
    {
        _avail = available;
        bool compact = MeasureFull() > available;
        if (compact != _compact) { _compact = compact; Invalidate(); }
        Width = Math.Min(available, Measure());
        return Width;
    }

    // width of the strip with every label shown (what decides whether we go compact at all)
    private int MeasureFull()
    {
        int w = Inset * 2;
        for (int i = 0; i < _labels.Length; i++)
            w += GlyphW(i) + TextRenderer.MeasureText(_labels[i], SegFont).Width + SegPadX * 2;
        return w;
    }

    private int GlyphW(int i) =>
        _glyphs is { } gl && gl[i].Length > 0
            ? TextRenderer.MeasureText(gl[i], GlyphFont, Size.Empty, TextFormatFlags.NoPadding).Width + GlyphGap
            : 0;

    // Compact mode: the active segment always keeps its label, and the hovered one expands to
    // show it in place (collapsing again on leave). Everything else is an icon.
    private bool ShowsLabel(int i) => !_compact || i == _active || i == _hover || GlyphW(i) == 0;

    private int SegW(int i)
    {
        int gw = GlyphW(i);
        int lw = ShowsLabel(i) ? TextRenderer.MeasureText(_labels[i], SegFont).Width : 0;
        // an icon-only segment keeps the glyph's trailing gap out of the padding
        return gw + lw + SegPadX * 2 - (ShowsLabel(i) || gw == 0 ? 0 : GlyphGap);
    }

    private int Measure()
    {
        int w = Inset * 2;
        for (int i = 0; i < _labels.Length; i++) w += SegW(i);
        return w;
    }

    /// <summary>Programmatic selection. Set <paramref name="raise"/> to fire <see cref="Changed"/>.</summary>
    public void SetActive(int i, bool raise = false)
    {
        i = Math.Clamp(i, 0, _labels.Length - 1);
        if (i == _active) { if (raise) Changed?.Invoke(i); return; }
        _active = i;
        // in compact mode the active segment carries the label, so the widths shift with it
        if (_compact) Width = Math.Min(Width, Measure());
        Invalidate();
        if (raise) Changed?.Invoke(i);
    }

    private RectangleF[] Segments()
    {
        var rects = new RectangleF[_labels.Length];
        float x = Inset, y = Inset, h = Height - Inset * 2;
        for (int i = 0; i < _labels.Length; i++)
        {
            float w = SegW(i);
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
        if (h != _hover) { _hover = h; SyncWidth(); Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1) { _hover = -1; SyncWidth(); Invalidate(); }
    }

    // In compact mode the hovered segment grows by its label, so the strip's own width follows -
    // clamped to what the page gave us, so expanding can never push a scrollbar back.
    private void SyncWidth()
    {
        if (!_compact) return;
        int w = Math.Min(_avail, Measure());
        if (w != Width) Width = w;
    }

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
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;   // clean 1.4px accent stroke on the active segment
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
            int gw = GlyphW(i);
            if (gw > 0 && !ShowsLabel(i))
            {
                // compact: icon only, centred - the name comes back on hover (ShowsLabel)
                TextRenderer.DrawText(g, _glyphs![i], GlyphFont, Rectangle.Round(r), col,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else if (gw > 0)
            {
                // glyph + label centred together: glyph first, label right of it
                int lw = TextRenderer.MeasureText(_labels[i], SegFont).Width;
                int left = (int)(r.X + (r.Width - gw - lw) / 2);
                TextRenderer.DrawText(g, _glyphs![i], GlyphFont,
                    new Rectangle(left, (int)r.Y, gw - GlyphGap, (int)r.Height), col,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, _labels[i], SegFont,
                    new Rectangle(left + gw, (int)r.Y, lw + 4, (int)r.Height), col,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else
                TextRenderer.DrawText(g, _labels[i], SegFont, Rectangle.Round(r), col,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
