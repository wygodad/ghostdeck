using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// The Dashboard's one drawn surface. Widgets are not controls: every system window costs
/// (the Settings page has 392, measured in v1.37), so the whole tab paints into this single
/// control and the widgets are rectangles with click zones (the WinPowerBody pattern).
///
/// Rendering follows the Status canvas: the content is rendered once into a persistent
/// <see cref="BufferedGraphics"/> allocated from this control's own Graphics, so the offscreen
/// DC is DPI-aware and TextRenderer draws exactly as on screen. The page scrolls this control
/// natively, so scrolling is a blit and no text has to honour a translate transform.
///
/// Step 1 of the build: the header and the empty grid. The widget layer, the static bitmaps
/// per widget and the live tick follow in the next steps (DOC/GhostDeck-dashboard-etap1-kroki.md).
/// </summary>
internal sealed class DashCanvas : Control
{
    private readonly MainDeps _d;
    private BufferedGraphics? _buf;
    private int _bufW, _bufH;

    public DashCanvas(MainDeps d)
    {
        _d = d;
        BackColor = Theme.Surface;
        ResizeRedraw = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    private int S(int v) => (int)Math.Ceiling(v * Dpi.Of(this) / 96f);

    // ---- grid geometry ---------------------------------------------------------------
    // Six columns of square cells. The cell is cut from the width, so the grid fills the
    // window at any scale; when the window cannot give every column its minimum, the grid
    // drops columns and the widgets wrap in their order (the layout stores order and size,
    // never a position).
    public const int MaxColumns = 6;
    public const int MinColumns = 2;
    private int Pad => S(28);
    private int Gap => S(16);
    private int MinCell => S(120);
    private const int PlaceholderRows = 2;   // step 1 only: the empty grid shows two rows of cells

    public int Columns(int width)
    {
        int avail = width - Pad * 2;
        return Math.Clamp((avail + Gap) / (MinCell + Gap), MinColumns, MaxColumns);
    }

    public int Cell(int width)
    {
        int cols = Columns(width);
        return Math.Max(1, (width - Pad * 2 - Gap * (cols - 1)) / cols);
    }

    private Font TitleFont() => Dpi.Font("Segoe UI", 18f, FontStyle.Bold);

    /// <summary>Y of the first grid row: the title band and a clear gap under it.</summary>
    private int GridTop()
    {
        using var f = TitleFont();
        return S(24) + f.Height + S(28);
    }

    /// <summary>Height the page sizes this control to (the page scrolls it when taller than the window).</summary>
    public int ContentHeight(int width)
    {
        int cell = Cell(width);
        return GridTop() + PlaceholderRows * cell + (PlaceholderRows - 1) * Gap + Pad;
    }

    // ---- buffer ---------------------------------------------------------------------
    public void Rebuild() { RenderToBuffer(); Invalidate(); }

    private void RenderToBuffer()
    {
        if (Width <= 0 || Height <= 0) return;
        using var cg = CreateGraphics();   // DPI-aware DC of this control
        if (_buf == null || _bufW != Width || _bufH != Height)
        {
            _buf?.Dispose();
            var ctx = BufferedGraphicsManager.Current;
            ctx.MaximumBuffer = new Size(Width + 1, Height + 1);
            _buf = ctx.Allocate(cg, new Rectangle(0, 0, Width, Height));
            _bufW = Width; _bufH = Height;
        }
        var g = _buf!.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Surface);
        if (_d.Settings.ShowGrid) Ui.DrawGrid(g, new Rectangle(0, 0, Width, Height));
        Render(g, Width);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }   // the buffer covers the whole surface

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_buf == null || _bufW != Width || _bufH != Height) RenderToBuffer();
        _buf?.Render(e.Graphics);
    }

    /// <summary>Free the offscreen buffer (the page is hidden); OnPaint rebuilds it on demand.</summary>
    internal void ReleaseBuffer()
    {
        _buf?.Dispose();
        _buf = null;
        _bufW = _bufH = 0;
    }

    protected override void Dispose(bool disposing) { if (disposing) _buf?.Dispose(); base.Dispose(disposing); }

    // ---- painting -------------------------------------------------------------------
    private void Render(Graphics g, int width)
    {
        using (var tf = TitleFont())
            TextRenderer.DrawText(g, Lang.T("tab_dashboard"), tf, new Point(Pad, S(24)), Theme.Text);

        int cols = Columns(width), cell = Cell(width), gap = Gap, top = GridTop();
        using var pen = new Pen(Theme.Border) { DashStyle = DashStyle.Dash };
        float radius = S(12);
        for (int row = 0; row < PlaceholderRows; row++)
            for (int col = 0; col < cols; col++)
            {
                var r = new RectangleF(Pad + col * (cell + gap), top + row * (cell + gap), cell, cell);
                using var path = Theme.RoundRect(r, radius);
                g.DrawPath(pen, path);
            }
    }
}
