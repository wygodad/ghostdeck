using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// The Dashboard's one drawn surface. Widgets are not controls: every system window costs
/// (the Settings page has 392, measured in v1.37), so the whole tab paints into this single
/// control and the widgets are rectangles with click zones (the WinPowerBody pattern).
///
/// Two layers:
/// <list type="bullet">
/// <item>the <b>static layer</b> (background, title, every widget's <see cref="IDashWidget.PaintStatic"/>:
/// dials, scales, titles) lives in one persistent <see cref="BufferedGraphics"/> allocated from
/// this control's own Graphics, so its DC is DPI-aware and TextRenderer draws exactly as on
/// screen (docs/RENDERING.md §2 and §4: a plain Bitmap would render text at 96 DPI). It is
/// rendered again only when the size, the scale, the theme, the language, the grid setting or
/// the layout change;</item>
/// <item>the <b>live layer</b> (<see cref="IDashWidget.PaintLive"/>: needles, numbers, operating
/// points) is painted in OnPaint over the blitted static buffer, for the widgets inside the
/// paint clip only. A tick asks every widget <see cref="IDashWidget.LiveChanged"/> and
/// invalidates just the rectangles that answered yes; nothing moved = nothing painted.</item>
/// </list>
/// The page scrolls this control natively, so scrolling is a blit and no text has to honour a
/// translate transform. Step 3 of the build (DOC/GhostDeck-dashboard-etap1-kroki.md).
/// </summary>
internal sealed class DashCanvas : Control
{
    private readonly MainDeps _d;
    private readonly DashContext _ctx;

    // the static layer and the key it was rendered for
    private BufferedGraphics? _static;
    private int _sW, _sH, _sDpi;
    private bool _sDark, _sGrid, _staticDirty = true;
    private string _sLang = "";

    // the layout: the items (the settings list itself, so later edits are in place), one
    // widget per item, their placement for the current column count and the pixel rectangles
    private IReadOnlyList<DashItem> _items = Array.Empty<DashItem>();
    private readonly List<IDashWidget> _widgets = new();
    private List<DashPlaced> _placed = new();
    private Rectangle[] _rects = Array.Empty<Rectangle>();
    private int _placedCols = -1, _rectsCell = -1, _rectsTop = -1;

    public DashCanvas(MainDeps d)
    {
        _d = d;
        _ctx = new DashContext(d, this) { RequestRebuild = Rebuild };
        BackColor = Theme.Surface;
        ResizeRedraw = true;
        // OptimizedDoubleBuffer: OnPaint composes the static blit and the live layer offscreen
        // and presents once, so a tick never shows the static layer without its needles.
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
        _staticDirty = true;
    }

    private List<DashPlaced> Placement(int width)
    {
        int cols = Columns(width);
        if (cols != _placedCols || _placed.Count != _items.Count)
        {
            _placed = DashLayout.Place(_items, _widgets, cols);
            _placedCols = cols;
            _rectsCell = -1;
            _staticDirty = true;
        }
        return _placed;
    }

    /// <summary>Pixel rectangles of the placed widgets for this control's current width (cached per cell size).</summary>
    private Rectangle[] Rects()
    {
        var placed = Placement(Width);
        int cell = Cell(Width), top = GridTop(), gap = Gap, pad = Pad;
        if (_rects.Length == placed.Count && _rectsCell == cell && _rectsTop == top) return _rects;
        var rects = new Rectangle[placed.Count];
        for (int i = 0; i < placed.Count; i++)
        {
            var c = placed[i].Cells;
            rects[i] = new Rectangle(pad + c.X * (cell + gap), top + c.Y * (cell + gap),
                                     c.Width * cell + (c.Width - 1) * gap, c.Height * cell + (c.Height - 1) * gap);
        }
        _rects = rects; _rectsCell = cell; _rectsTop = top;
        _staticDirty = true;
        return rects;
    }

    // ---- ticks ------------------------------------------------------------------------
    /// <summary>Widgets repainted by the last tick and the area they covered (the preview tool's bench reads them).</summary>
    internal int LastLiveRepaints { get; private set; }
    internal Rectangle LastLiveRegion { get; private set; }

    /// <summary>
    /// A new data sample (read off the window thread by the page). Every widget says whether
    /// its live layer changed; only those rectangles are invalidated. The static layer is not
    /// touched unless its key changed (theme, language, grid setting, scale).
    /// </summary>
    public void SetSample(DashSample s)
    {
        _ctx.Sample = s;
        if (!Visible || Width <= 0) return;
        if (StaticKeyChanged()) { _staticDirty = true; Invalidate(); LastLiveRepaints = _widgets.Count; LastLiveRegion = ClientRectangle; return; }
        var rects = Rects();
        int n = 0;
        var region = Rectangle.Empty;
        for (int i = 0; i < _placed.Count; i++)
        {
            if (!_placed[i].Widget.LiveChanged(_ctx)) continue;
            Invalidate(rects[i]);
            region = n == 0 ? rects[i] : Rectangle.Union(region, rects[i]);
            n++;
        }
        LastLiveRepaints = n;
        LastLiveRegion = region;
    }

    private bool StaticKeyChanged() =>
        _static == null || _sW != Width || _sH != Height || _sDpi != Dpi.Of(this) || _sDark != Theme.Dark
        || _sGrid != _d.Settings.ShowGrid || _sLang != Lang.CurrentCode;

    // ---- static layer -----------------------------------------------------------------
    /// <summary>Marks the static layer stale and repaints (theme, language, layout edits).</summary>
    public void Rebuild() { _staticDirty = true; Invalidate(); }

    private void EnsureStatic()
    {
        if (Width <= 0 || Height <= 0) return;
        if (!_staticDirty && !StaticKeyChanged()) return;
        using var cg = CreateGraphics();   // DPI-aware DC of this control
        if (_static == null || _sW != Width || _sH != Height)
        {
            _static?.Dispose();
            var ctx = BufferedGraphicsManager.Current;
            ctx.MaximumBuffer = new Size(Width + 1, Height + 1);
            _static = ctx.Allocate(cg, new Rectangle(0, 0, Width, Height));
        }
        var g = _static!.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Surface);
        if (_d.Settings.ShowGrid) Ui.DrawGrid(g, new Rectangle(0, 0, Width, Height));
        using (var tf = TitleFont())
            TextRenderer.DrawText(g, Lang.T("tab_dashboard"), tf, new Point(Pad, S(24)), Theme.Text);
        Sync();
        var rects = Rects();
        for (int i = 0; i < _placed.Count; i++) _placed[i].Widget.PaintStatic(g, rects[i], _ctx);
        _sW = Width; _sH = Height; _sDpi = Dpi.Of(this); _sDark = Theme.Dark; _sGrid = _d.Settings.ShowGrid; _sLang = Lang.CurrentCode;
        _staticDirty = false;
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }   // the static layer covers the whole surface

    protected override void OnPaint(PaintEventArgs e)
    {
        EnsureStatic();
        if (_static == null) return;
        _static.Render(e.Graphics);   // the whole buffer; the target's clip limits what reaches the screen
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rects = Rects();
        var clip = e.ClipRectangle;
        for (int i = 0; i < _placed.Count; i++)
            if (rects[i].IntersectsWith(clip)) _placed[i].Widget.PaintLive(e.Graphics, rects[i], _ctx);
    }

    // ---- clicks -----------------------------------------------------------------------
    private int HitWidget(Point p)
    {
        if (_placed.Count == 0 || _rects.Length != _placed.Count) return -1;
        for (int i = 0; i < _rects.Length; i++) if (_rects[i].Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var want = HitWidget(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
        if (Cursor != want) Cursor = want;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        int i = HitWidget(e.Location);
        if (i >= 0) _placed[i].Widget.Click(e.Location, _rects[i], _ctx);
    }

    /// <summary>
    /// Preview tool only: one paint pass into an offscreen buffer of the given clip. The tool's
    /// window sits off screen, so it never receives WM_PAINT and Update() measures nothing; this
    /// runs the same OnPaint (static blit + live layer, and the static render when it is stale).
    /// </summary>
    internal void BenchPaint(Rectangle clip)
    {
        if (clip.Width <= 0 || clip.Height <= 0) return;
        using var cg = CreateGraphics();
        using var buf = BufferedGraphicsManager.Current.Allocate(cg, clip);
        buf.Graphics.SetClip(clip);
        OnPaint(new PaintEventArgs(buf.Graphics, clip));
    }

    /// <summary>Free the static buffer (the page is hidden); OnPaint renders it again on demand.</summary>
    internal void ReleaseBuffer()
    {
        _static?.Dispose();
        _static = null;
        _sW = _sH = 0;
        _staticDirty = true;
    }

    protected override void Dispose(bool disposing) { if (disposing) _static?.Dispose(); base.Dispose(disposing); }
}
