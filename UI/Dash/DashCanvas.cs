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
/// Step 2 of the build: the layout from settings.json placed on the grid, every item painted
/// by its widget (placeholders until step 4). The static bitmaps per widget and the live tick
/// follow in step 3 (DOC/GhostDeck-dashboard-etap1-kroki.md).
/// </summary>
internal sealed class DashCanvas : Control
{
    private readonly MainDeps _d;
    private readonly DashContext _ctx;
    private BufferedGraphics? _buf;
    private int _bufW, _bufH;

    // the layout: the items (the settings list itself, so later edits are in place), one
    // widget per item, and their placement for the current column count
    private IReadOnlyList<DashItem> _items = Array.Empty<DashItem>();
    private readonly List<IDashWidget> _widgets = new();
    private List<DashPlaced> _placed = new();
    private int _placedCols = -1;

    public DashCanvas(MainDeps d)
    {
        _d = d;
        _ctx = new DashContext(d, this);
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

    /// <summary>Pixel rectangle of a placed item for a given cell size.</summary>
    private Rectangle PixelRect(Rectangle cells, int cell, int top)
    {
        int gap = Gap;
        return new Rectangle(
            Pad + cells.X * (cell + gap), top + cells.Y * (cell + gap),
            cells.Width * cell + (cells.Width - 1) * gap, cells.Height * cell + (cells.Height - 1) * gap);
    }

    /// <summary>Height the page sizes this control to (the page scrolls it when taller than the window).</summary>
    public int ContentHeight(int width)
    {
        Sync();
        var placed = Placement(width);
        int rows = Math.Max(1, DashLayout.Rows(placed)), cell = Cell(width);
        return GridTop() + rows * cell + (rows - 1) * Gap + Pad;
    }

    // ---- layout -----------------------------------------------------------------------
    /// <summary>Re-reads the layout from the settings; widgets are recreated only when the types changed.</summary>
    public void Sync()
    {
        var items = DashLayout.ItemsOf(_d.Settings);
        bool same = ReferenceEquals(items, _items) && items.Count == _widgets.Count;
        if (same)
            for (int i = 0; i < items.Count; i++)
                if (items[i].Type != _widgets[i].TypeId) { same = false; break; }
        if (same) return;
        _items = items;
        _widgets.Clear();
        foreach (var it in items) _widgets.Add(DashRegistry.Make(it));
        _placedCols = -1;
    }

    private List<DashPlaced> Placement(int width)
    {
        int cols = Columns(width);
        if (cols != _placedCols || _placed.Count != _items.Count)
        {
            _placed = DashLayout.Place(_items, _widgets, cols);
            _placedCols = cols;
        }
        return _placed;
    }

    /// <summary>A new data sample arrived (the page reads it off the window thread).</summary>
    public void SetSample(DashSample s)
    {
        _ctx.Sample = s;
        if (Visible) Rebuild();
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

        Sync();
        int cell = Cell(width), top = GridTop();
        foreach (var p in Placement(width))
        {
            var r = PixelRect(p.Cells, cell, top);
            p.Widget.PaintStatic(g, r, _ctx);
            p.Widget.PaintLive(g, r, _ctx);
        }
    }
}
