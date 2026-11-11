using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// The Dashboard's one drawn surface. Widgets are not controls: every system window costs
/// (the Settings page has 392, measured in v1.37), so the whole tab paints into this single
/// control and the widgets are rectangles with click zones (the WinPowerBody pattern).
///
/// Two layers:
/// <list type="bullet">
/// <item>the <b>static layer</b> (background, title and header buttons, the catalogue panel,
/// every widget's <see cref="IDashWidget.PaintStatic"/>: dials, scales, titles) lives in one
/// persistent <see cref="BufferedGraphics"/> allocated from this control's own Graphics, so its
/// DC is DPI-aware and TextRenderer draws exactly as on screen (docs/RENDERING.md §2 and §4: a
/// plain Bitmap would render text at 96 DPI). It is rendered again only when the size, the
/// scale, the theme, the language, the grid setting, the layout or the edit state change;</item>
/// <item>the <b>live layer</b> (<see cref="IDashWidget.PaintLive"/>: needles, numbers, operating
/// points, and in edit mode the frames, buttons and the dragged card) is painted in OnPaint over
/// the blitted static buffer, for the widgets inside the paint clip only. A tick asks every
/// widget <see cref="IDashWidget.LiveChanged"/> and invalidates just the rectangles that
/// answered yes; nothing moved = nothing painted.</item>
/// </list>
/// Edit mode ("Edit layout"): every widget gets a dashed frame, a remove button, a size button
/// that cycles its allowed sizes and can be dragged to a new place with the layout re-flowing
/// live; the catalogue panel on the right adds widgets; "Restore default" asks first. Every
/// change is saved at once. The page scrolls this control natively.
/// </summary>
internal sealed partial class DashCanvas : Control
{
    private readonly MainDeps _d;
    private readonly DashContext _ctx;

    // the static layer and the key it was rendered for
    private BufferedGraphics? _static;
    private int _sW, _sH, _sDpi;
    private int _bandTop, _bandH;   // the rows of the canvas the static buffer holds (never the whole of a tall canvas)
    private bool _sDark, _sGrid, _sStats, _staticDirty = true;
    private string _sLang = "";
    private object? _sSummary;   // the summary the static layer was painted with (reference)

    // the layout: the items (the settings list itself, so edits are in place), one widget per
    // item, their placement for the current column count and the pixel rectangles
    private List<DashItem> _items = new();
    private readonly List<IDashWidget> _widgets = new();
    private Dictionary<DashItem, IDashWidget> _byItem = new(ReferenceEqualityComparer.Instance);   // the instance of every layout item
    private List<DashPlaced> _placed = new();
    private Rectangle[] _rects = Array.Empty<Rectangle>();
    private int _placedCols = -1, _rectsCell = -1, _rectsTop = -1, _rectsGridW = -1;

    // edit mode
    private bool _edit, _catalog;
    private bool _sortPopular = true;                                   // catalogue order while a summary is in: by share, else by name
    private readonly Dictionary<string, Rectangle> _starRows = new();   // the five stars of a catalogue row, by type (hover repaint)
    private enum Zone { None, Edit, Done, Add, Reset, Layouts, Refresh, Remove, Size, CatalogAdd, Widget, Sort, Star, More, Info, View, Map, StatsOn, StatsInfo, Bar, Cols, Row, Tile, StatsRefresh }
    private readonly record struct Hot(Zone Kind, int Index, string? Type);
    private Hot _hot = new(Zone.None, -1, null);
    private readonly List<(Rectangle r, Hot zone)> _zones = new();
    private Point _downAt;
    private int _pressIndex = -1;   // widget under the mouse button in edit mode (a drag candidate)
    private int _dragIndex = -1;    // widget being dragged
    private Point _dragPos;
    private Size _dragOffset;

    public DashCanvas(MainDeps d)
    {
        _d = d;
        _ctx = new DashContext(d, this) { RequestRebuild = Rebuild, CellPx = () => Width > 0 ? Cell(Width) : 0 };
        BackColor = Theme.Surface;
        ResizeRedraw = true;
        // OptimizedDoubleBuffer: OnPaint composes the static blit and the live layer offscreen
        // and presents once, so a tick never shows the static layer without its needles.
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Stats.SummaryChanged += OnSummaryChanged;   // a fresh weekly summary: the catalogue's badges and order change
    }

    private void OnSummaryChanged()
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(Rebuild); else Rebuild();
    }

    private int S(int v) => (int)Math.Ceiling(v * Dpi.Of(this) / 96f);
    private float Sf(float v) => v * Dpi.Of(this) / 96f;

    // ---- grid geometry ---------------------------------------------------------------
    // Six columns of square cells. The cell is cut from the width, so the grid fills the
    // window at any scale; when the window cannot give every column its minimum, the grid
    // drops columns and the widgets wrap in their order (the layout stores order and size,
    // never a position). With the catalogue open the grid gives up the panel's width.
    public const int MaxColumns = 6;
    public const int MinColumns = 2;
    private int Pad => S(28);
    private int Gap => S(16);
    private int MinCell => S(150);   // below this a 1x1 ring loses its label and a 2x1 toggle its captions
    private int PanelW => S(300);

    private int GridWidth(int width) => _catalog ? width - PanelW - Gap : width;

    public int Columns(int width)
    {
        int avail = GridWidth(width) - Pad * 2;
        int auto = Math.Clamp((avail + Gap) / (MinCell + Gap), MinColumns, MaxColumns);
        int want = _d.Settings.DashColumns;
        if (want <= 0) return auto;
        // a chosen density (edit mode): as asked while a cell keeps at least 110 px; a narrower window gives fewer
        int fit = Math.Max(MinColumns, (avail + Gap) / (S(110) + Gap));
        return Math.Clamp(Math.Min(want, fit), MinColumns, 8);
    }

    public int Cell(int width)
    {
        int cols = Columns(width);
        return Math.Max(1, (GridWidth(width) - Pad * 2 - Gap * (cols - 1)) / cols);
    }

    private Font TitleFont() => Dpi.Font("Segoe UI", 18f, FontStyle.Bold);
    private Font ButtonFont() => _ctx.Font(10f, FontStyle.Bold);
    private int ButtonH => Math.Max(S(32), ButtonFont().Height + S(12));

    /// <summary>Y of the first grid row: the title band and a clear gap under it.</summary>
    private int GridTop()
    {
        using var f = TitleFont();
        return S(24) + Math.Max(Math.Max(f.Height, ButtonH), SwitchH) + S(28) + (HeaderWraps ? ButtonH + S(10) : 0) + (_edit ? ColsRowH + S(10) : 0);   // a second row for the buttons when they do not fit beside the switch; edit mode adds the columns row
    }

    /// <summary>Height the page sizes this control to (the page scrolls it when taller than the window).</summary>
    public int ContentHeight(int width)
    {
        Sync();
        if (_view == View.Catalog) return GridTop() + CatalogViewHeight(width) + Pad;
        if (_view == View.Stats) return GridTop() + StatsViewHeight(width) + Pad;
        var placed = Placement(width);
        int rows = Math.Max(1, DashLayout.Rows(placed)), cell = Cell(width);
        int grid = GridTop() + rows * cell + (rows - 1) * Gap + Pad;
        int panel = _catalog ? GridTop() + CatalogHeight(width) + Pad : 0;
        return Math.Max(grid, panel);
    }

    // ---- layout -----------------------------------------------------------------------
    /// <summary>
    /// Re-reads the layout from the settings. A widget instance belongs to its layout item (the
    /// object, not the type: two items of one type are two widgets with their own settings) and
    /// is kept across reorders, removals and additions, so a curve widget keeps its tables and a
    /// ring keeps what it last painted; only new items get new instances.
    /// </summary>
    public void Sync()
    {
        var items = DashLayout.ItemsOf(_d.Settings);
        bool same = ReferenceEquals(items, _items) && items.Count == _widgets.Count;
        if (same)
            for (int i = 0; i < items.Count; i++)
                if (!_byItem.TryGetValue(items[i], out var w) || !ReferenceEquals(w, _widgets[i])) { same = false; break; }
        if (same) return;
        _widgets.Clear();
        var kept = new Dictionary<DashItem, IDashWidget>(ReferenceEqualityComparer.Instance);
        int cells = 0, keep = items.Count;
        for (int n = 0; n < items.Count; n++)
        {
            var it = items[n];
            if (!_byItem.TryGetValue(it, out var w) || w.TypeId != it.Type)
            {
                w = DashRegistry.Make(it);
                // A widget stands in a size it allows. A layout that says otherwise (a file edited
                // by hand, settings changed outside the card) would have it paint over its
                // neighbours, so it takes the nearest size it allows. A type this version does
                // not know keeps its size untouched.
                if (w is not UnknownWidget) (it.W, it.H) = DashItem.Fit(w.Sizes, it.W, it.H);
            }
            // The area's bound counts the sizes the widgets take here, after the fit: settings.json
            // is checked on the sizes it states, and a widget written smaller than it can be would
            // slip under the bound and then grow. What is over the bound goes from the end. A layout
            // file never gets this far (ImportLayout refuses it whole), and every action in the app
            // checks for room first, so this is for a settings file changed by hand.
            cells += it.W * it.H;
            if (cells > DashItem.MaxCells) { keep = n; break; }
            kept[it] = w;
            _widgets.Add(w);
        }
        if (keep < items.Count) items.RemoveRange(keep, items.Count - keep);
        _byItem = kept;
        _items = items;
        _placedCols = -1;
        _staticDirty = true;
    }

    private List<DashPlaced> Placement(int width)
    {
        int cols = Columns(width);
        if (cols != _placedCols || _placed.Count != _items.Count || _rectsGridW != GridWidth(width))
        {
            _placed = DashLayout.Place(_items, _widgets, cols);
            _placedCols = cols;
            _rectsCell = -1;
            _staticDirty = true;
        }
        return _placed;
    }

    /// <summary>Forces a new placement (an item moved or changed size) and a static render.</summary>
    private void Replace()
    {
        _placedCols = -1;
        _rectsCell = -1;
        _staticDirty = true;
        Invalidate();
    }

    /// <summary>Pixel rectangles of the placed widgets for this control's current width (cached per cell size).</summary>
    private Rectangle[] Rects()
    {
        var placed = Placement(Width);
        int cell = Cell(Width), top = GridTop(), gap = Gap, pad = Pad, gw = GridWidth(Width);
        if (_rects.Length == placed.Count && _rectsCell == cell && _rectsTop == top && _rectsGridW == gw) return _rects;
        var rects = new Rectangle[placed.Count];
        for (int i = 0; i < placed.Count; i++)
        {
            var c = placed[i].Cells;
            rects[i] = new Rectangle(pad + c.X * (cell + gap), top + c.Y * (cell + gap),
                                     c.Width * cell + (c.Width - 1) * gap, c.Height * cell + (c.Height - 1) * gap);
        }
        _rects = rects; _rectsCell = cell; _rectsTop = top; _rectsGridW = gw;
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
        if (!Visible || Width <= 0 || _view != View.Dashboard) return;
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
        || _sGrid != _d.Settings.ShowGrid || _sLang != Lang.CurrentCode
        || (_catalog && (_sStats != _d.Settings.StatsEnabled || !ReferenceEquals(_sSummary, Stats.Current)));

    // ---- static layer -----------------------------------------------------------------
    /// <summary>Marks the static layer stale and repaints (theme, language, layout edits).</summary>
    public void Rebuild() { _staticDirty = true; Invalidate(); }

    // The static layer is a buffer for a BAND of the canvas, not for all of it (1.45). The canvas
    // is as tall as its content and the page scrolls it, so a buffer for the whole canvas grew
    // with the layout: 200 widgets of the 2x2 size were 300 MB, and a layout file could ask for
    // gigabytes. The band is what is on screen and half of that again above and below, so its
    // size follows the window, never the layout; scrolling out of it renders the next band (only
    // the widgets in it, a few milliseconds).
    //
    // The painting code keeps using canvas coordinates. Shapes get there through the Graphics'
    // transform. Texts are GDI (TextRenderer), which ignores that transform, so for the time of
    // the static pass the buffer's device context gets a viewport origin: GDI honours it and GDI+
    // does not, which is exactly the split needed (measured outside the app before this was built).
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool SetViewportOrgEx(IntPtr hdc, int x, int y, IntPtr prev);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);

    /// <summary>Where GDI text lands on a buffer's graphics: (0, 0) of the painting code's coordinates at (x, y) of the buffer.</summary>
    private static void SetTextOrigin(Graphics g, int x, int y)
    {
        IntPtr hdc = g.GetHdc();
        try { SetViewportOrgEx(hdc, x, y, IntPtr.Zero); }
        finally { g.ReleaseHdc(hdc); }
    }

    /// <summary>The part of the canvas the page shows now.</summary>
    private Rectangle VisibleRect()
    {
        int top = Math.Clamp(-Top, 0, Math.Max(0, Height - 1));
        int shown = Parent is { } p ? p.ClientSize.Height : Height;
        return new Rectangle(0, top, Width, Math.Max(1, Math.Min(Height - top, Math.Max(1, shown))));
    }

    /// <summary>The tallest band a buffer is made for: twice what the page shows, and room for a small window's worth at least.</summary>
    private int MaxBandH => Math.Max(S(1600), VisibleRect().Height * 2);

    /// <summary>The band for a paint of <paramref name="need"/>: it, with what is on screen when the two touch, and half of that again on both sides.</summary>
    private (int top, int h) Band(Rectangle need)
    {
        var vis = VisibleRect();
        var core = vis.IntersectsWith(need) ? Rectangle.Union(vis, need) : need;
        int margin = Math.Max(0, Math.Min(core.Height / 2, (MaxBandH - core.Height) / 2));
        int top = Math.Max(0, core.Top - margin), bottom = Math.Min(Height, core.Bottom + margin);
        return (top, Math.Max(1, bottom - top));
    }

    /// <summary>Makes sure the static buffer holds the rows of <paramref name="need"/>, rendering a band around them when it does not or when it is stale.</summary>
    private void EnsureStatic(Rectangle need)
    {
        if (Width <= 0 || Height <= 0) return;
        need = Rectangle.Intersect(need, ClientRectangle);
        if (need.Height <= 0) need = VisibleRect();
        bool holds = _static != null && need.Top >= _bandTop && need.Bottom <= _bandTop + _bandH;
        if (holds && !_staticDirty && !StaticKeyChanged()) return;
        Sync();
        var rects = Rects();
        var (top, h) = Band(need);
        using var cg = CreateGraphics();   // DPI-aware DC of this control
        if (_static == null || _sW != Width || _bandH != h)
        {
            _static?.Dispose();
            var ctx = BufferedGraphicsManager.Current;
            ctx.MaximumBuffer = new Size(Width + 1, h + 1);
            _static = ctx.Allocate(cg, new Rectangle(0, 0, Width, h));
        }
        _bandTop = top; _bandH = h;
        var g = _static!.Graphics;
        g.ResetTransform();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Surface);
        g.TranslateTransform(0, -top);
        SetTextOrigin(g, 0, -top);
        try
        {
            // the grid's lines are counted from the canvas's top, whatever band is drawn
            if (_d.Settings.ShowGrid)
            {
                const int pitch = 48;
                int gridTop = Math.Max(0, top / pitch * pitch - pitch);
                Ui.DrawGrid(g, new Rectangle(0, gridTop, Width, Math.Min(Height, top + h) - gridTop));
            }
            _zones.Clear();
            PaintHeader(g);
            if (_view == View.Catalog) PaintCatalogView(g);
            else if (_view == View.Stats) PaintStatsView(g);
            else
            {
                for (int i = 0; i < _placed.Count; i++)
                    if (rects[i].Bottom > top && rects[i].Top < top + h) _placed[i].Widget.PaintStatic(g, rects[i], _ctx);
                if (_placed.Count == 0)
                {
                    int gw = GridWidth(Width);
                    DashPaint.Note(g, new Rectangle(Pad, GridTop(), gw - Pad * 2, Math.Max(S(40), Cell(Width))), _ctx, Lang.T("dash_empty"));
                }
                if (_catalog) PaintCatalog(g);
            }
        }
        finally
        {
            SetTextOrigin(g, 0, 0);
            g.ResetTransform();
        }
        _sW = Width; _sH = Height; _sDpi = Dpi.Of(this); _sDark = Theme.Dark; _sGrid = _d.Settings.ShowGrid; _sLang = Lang.CurrentCode;
        _sStats = _d.Settings.StatsEnabled; _sSummary = Stats.Current;
        _staticDirty = false;
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }   // the static layer covers the whole surface

    protected override void OnPaint(PaintEventArgs e) => PaintAll(e.Graphics, e.ClipRectangle);

    /// <summary>The static layer of <paramref name="clip"/> from the band buffer, then the live layer over it.</summary>
    private void PaintAll(Graphics target, Rectangle clip)
    {
        clip = Rectangle.Intersect(clip, ClientRectangle);
        if (clip.Width <= 0 || clip.Height <= 0) return;
        // a paint taller than a band holds (the whole of a tall canvas asked for at once) goes in slices
        int slice = Math.Max(1, MaxBandH / 2);
        for (int y = clip.Top; y < clip.Bottom; y += slice)
        {
            var part = new Rectangle(clip.Left, y, clip.Width, Math.Min(slice, clip.Bottom - y));
            EnsureStatic(part);
            if (_static == null) return;
            IntPtr src = _static.Graphics.GetHdc();
            try
            {
                IntPtr dst = target.GetHdc();
                try { BitBlt(dst, part.X, part.Y, part.Width, part.Height, src, part.X, part.Y - _bandTop, 0x00CC0020 /* SRCCOPY */); }
                finally { target.ReleaseHdc(dst); }
            }
            finally { _static.Graphics.ReleaseHdc(src); }
        }
        target.SmoothingMode = SmoothingMode.AntiAlias;
        if (_view == View.Dashboard)
        {
            var rects = Rects();
            for (int i = 0; i < _placed.Count; i++)
                if (rects[i].IntersectsWith(clip)) _placed[i].Widget.PaintLive(target, rects[i], _ctx);
            if (_edit) PaintEditChrome(target, rects, clip);
        }
        else if (_view == View.Stats) PaintStatsLive(target);
        PaintHot(target);
    }

    /// <summary>
    /// Preview tool only: a rectangle of the canvas as a picture, painted by the same code as the
    /// screen. Any rectangle, at any height of the canvas, so the tool can check that a band far
    /// down paints what the band at the top would.
    /// </summary>
    internal Bitmap RenderRegion(Rectangle r)
    {
        using var cg = CreateGraphics();
        using var ctx = new BufferedGraphicsContext();
        using var buf = ctx.Allocate(cg, new Rectangle(0, 0, r.Width, r.Height));
        var g = buf.Graphics;
        g.Clear(Theme.Surface);
        g.TranslateTransform(-r.X, -r.Y);
        SetTextOrigin(g, -r.X, -r.Y);
        try { PaintAll(g, r); }
        finally { SetTextOrigin(g, 0, 0); g.ResetTransform(); }
        var bmp = new Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using var bg = Graphics.FromImage(bmp);
        buf.Render(bg);
        return bmp;
    }

    /// <summary>Preview tool only: the band the static buffer holds now and its size in bytes.</summary>
    internal (int top, int height, long bytes) BandInfo => (_bandTop, _bandH, _static == null ? 0 : (long)_sW * _bandH * 4);

    /// <summary>Free the static buffer (the page is hidden); OnPaint renders it again on demand.</summary>
    internal void ReleaseBuffer()
    {
        _static?.Dispose();
        _static = null;
        _sW = _sH = 0;
        _bandTop = _bandH = 0;
        _staticDirty = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { Stats.SummaryChanged -= OnSummaryChanged; _statsTimer?.Dispose(); _spotTimer?.Dispose(); _static?.Dispose(); }
        base.Dispose(disposing);
    }

    // ---- header -------------------------------------------------------------------------
    private void PaintHeader(Graphics g)
    {
        using var tf = TitleFont();
        int bandH = Math.Max(Math.Max(tf.Height, ButtonH), SwitchH), top = S(24);
        PaintViewSwitch(g, Pad, top, bandH);
        // at 250 % in a long language the three edit buttons do not fit beside the switch: second row, right-aligned
        int by = HeaderWraps ? top + bandH + S(10) : top, bandB = HeaderWraps ? ButtonH : bandH;
        int x = GridWidth(Width) - Pad;
        if (_view == View.Catalog) { if (Summary != null) PaintSortPills(g, x, by, bandB); }
        else if (_view == View.Stats)
        {
            // Refresh fetches the signed summary now (the view itself asks only when the copy is ten
            // minutes old); beside it, when the copy at hand was generated and when it was last asked for
            if (StatsRefreshShown)
            {
                x = HeaderButton(g, x, by, bandB, "", Lang.T("dash_st_refresh"), new Hot(Zone.StatsRefresh, -1, null), primary: false);
                string stamp = StatsStamp();
                if (stamp.Length > 0)
                {
                    var sf = _ctx.Font(9f);
                    int sw = TextRenderer.MeasureText(stamp, sf, Size.Empty, TextFormatFlags.NoPadding).Width;
                    int left = x - S(12) - sw;
                    if (HeaderWraps || left > Pad + SwitchW() + S(16))
                        TextRenderer.DrawText(g, stamp, sf, new Rectangle(left, by, sw + S(2), bandB), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }
        else if (_edit)
        {
            x = HeaderButton(g, x, by, bandB, "", Lang.T("dash_done"), new Hot(Zone.Done, -1, null), primary: true);
            x = HeaderButton(g, x - S(8), by, bandB, "", Lang.T("dash_reset"), new Hot(Zone.Reset, -1, null), primary: false);
            x = HeaderButton(g, x - S(8), by, bandB, "", Lang.T("dash_add"), new Hot(Zone.Add, -1, null), primary: false, active: _catalog);
            HeaderButton(g, x - S(8), by, bandB, "", Lang.T("dash_layouts"), new Hot(Zone.Layouts, -1, null), primary: false);
        }
        else HeaderButton(g, x, by, bandB, "", Lang.T("dash_edit"), new Hot(Zone.Edit, -1, null), primary: false);
        if (_edit && _view == View.Dashboard) PaintColsRow(g, by + bandB + S(12));
    }

    /// <summary>A header button right-aligned at <paramref name="right"/>; returns its left edge.</summary>
    private int HeaderButton(Graphics g, int right, int top, int bandH, string glyph, string text, Hot zone, bool primary, bool active = false)
    {
        var f = ButtonFont();
        var gf = _ctx.Font(10f);
        using var glyphFont = Dpi.Font("Segoe MDL2 Assets", 10f);
        int tw = TextRenderer.MeasureText(text, f, Size.Empty, TextFormatFlags.NoPadding).Width;
        int gw = S(16), h = ButtonH, w = S(12) + gw + S(8) + tw + S(14);
        var r = new Rectangle(right - w, top + (bandH - h) / 2, w, h);
        PaintButton(g, r, glyph, text, primary, active, hot: false);
        _zones.Add((r, zone));
        return r.Left;
    }

    private void PaintButton(Graphics g, Rectangle r, string glyph, string text, bool primary, bool active, bool hot)
    {
        using var path = Theme.RoundRect(r, Sf(8));
        var fill = primary ? Theme.AccentFill : active ? Theme.AccentSoft : hot ? Theme.Card : Theme.Surface;
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        using (var pen = new Pen(primary ? Theme.AccentFill : active || hot ? Theme.Accent : Theme.BorderStrong)) g.DrawPath(pen, path);
        var fg = primary ? Color.White : active ? Theme.Accent : Theme.Text;
        using var glyphFont = Dpi.Font("Segoe MDL2 Assets", 10f);
        int gw = S(16);
        TextRenderer.DrawText(g, glyph, glyphFont, new Rectangle(r.Left + S(12), r.Top, gw, r.Height), fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping);
        TextRenderer.DrawText(g, text, ButtonFont(), new Rectangle(r.Left + S(12) + gw + S(8), r.Top, r.Width - S(12) - gw - S(8), r.Height), fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    // ---- edit chrome ----------------------------------------------------------------------
    private int RemoveBtn => S(22);
    private int PillH => Math.Max(S(20), _ctx.Font(8.5f, FontStyle.Bold).Height + S(4));

    private Rectangle RemoveRect(Rectangle r) => new(r.Right - RemoveBtn - S(8), r.Top + S(8), RemoveBtn, RemoveBtn);
    private Rectangle GripRect(Rectangle r) => new(r.Left + S(8), r.Bottom - PillH - S(8), S(24), PillH);
    private Rectangle SizeRect(Rectangle r, int index)
    {
        string label = SizeLabel(index);
        int w = TextRenderer.MeasureText(label, _ctx.Font(8.5f, FontStyle.Bold), Size.Empty, TextFormatFlags.NoPadding).Width + S(22);
        return new Rectangle(r.Right - w - S(8), r.Bottom - PillH - S(8), w, PillH);
    }

    private string SizeLabel(int index) => index >= 0 && index < _items.Count ? $"{_items[index].W}×{_items[index].H}" : "";

    private void PaintEditChrome(Graphics g, Rectangle[] rects, Rectangle clip)
    {
        var f = _ctx.Font(8.5f, FontStyle.Bold);
        using var grip = Dpi.Font("Segoe UI Symbol", 9f);
        for (int i = 0; i < rects.Length; i++)
        {
            var r = rects[i];
            if (!r.IntersectsWith(clip)) continue;
            bool dragging = i == _dragIndex;
            // frame
            using (var path = Theme.RoundRect(RectangleF.Inflate(r, -1, -1), Sf(12)))
            using (var pen = new Pen(dragging ? Theme.Accent : Color.FromArgb(170, Theme.Accent), dragging ? 2f : 1.4f) { DashStyle = dragging ? DashStyle.Solid : DashStyle.Dash })
                g.DrawPath(pen, path);
            // two small pills at the bottom corners (a full bar hid the ring labels): the grip and the size button
            var gr = GripRect(r);
            using (var path = Theme.RoundRect(gr, Sf(5)))
            using (var b = new SolidBrush(Color.FromArgb(210, Theme.Surface))) g.FillPath(b, path);
            TextRenderer.DrawText(g, "⠿", grip, gr, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var sz = SizeRect(r, i);
            bool hotSize = _hot.Kind == Zone.Size && _hot.Index == i;
            using (var path = Theme.RoundRect(sz, Sf(5)))
            {
                using var b = new SolidBrush(hotSize ? Theme.AccentSoft : Theme.Card);
                g.FillPath(b, path);
                using var pen = new Pen(hotSize ? Theme.Accent : Theme.Border);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, SizeLabel(i) + " ▸", f, sz, hotSize ? Theme.Accent : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            // remove button
            var rm = RemoveRect(r);
            bool hotRm = _hot.Kind == Zone.Remove && _hot.Index == i;
            using (var b = new SolidBrush(hotRm ? Theme.Red : Color.FromArgb(220, Theme.Surface))) g.FillEllipse(b, rm);
            using (var pen = new Pen(hotRm ? Theme.Red : Theme.BorderStrong)) g.DrawEllipse(pen, rm);
            TextRenderer.DrawText(g, "✕", _ctx.Font(8f, FontStyle.Bold), rm, hotRm ? Color.White : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        if (_dragIndex >= 0 && _dragIndex < rects.Length)
        {
            // the card that follows the cursor: a translucent copy of the frame with the name
            var r = rects[_dragIndex];
            var ghost = new Rectangle(_dragPos.X - _dragOffset.Width, _dragPos.Y - _dragOffset.Height, r.Width, r.Height);
            using var path = Theme.RoundRect(ghost, Sf(12));
            using (var b = new SolidBrush(Color.FromArgb(150, Theme.Card))) g.FillPath(b, path);
            using (var pen = new Pen(Theme.Accent, 2f)) g.DrawPath(pen, path);
            TextRenderer.DrawText(g, Lang.T(_placed[_dragIndex].Widget.NameKey), _ctx.Font(10.5f, FontStyle.Bold), Rectangle.Inflate(ghost, -S(12), -S(12)), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Hover feedback for the header and catalogue buttons (the widgets' own buttons paint theirs in PaintEditChrome).</summary>
    private void PaintHot(Graphics g)
    {
        if (!_edit && _hot.Kind is Zone.Widget or Zone.More && _hot.Index >= 0 && _hot.Index < _rects.Length)
        {
            PaintMore(g, _rects[_hot.Index], _hot.Kind == Zone.More);
            return;
        }
        if (_hot.Kind == Zone.Map) { PaintMapHover(g); return; }
        if (_hot.Kind == Zone.Star)
        {
            // the stars up to the one under the cursor fill in the accent, over the card background
            if (_hot.Type != null && _starRows.TryGetValue(_hot.Type, out var row))
                PaintStars(g, row, _hot.Index, Theme.Accent, Theme.Faint, clear: true);
            return;
        }
        if (_hot.Kind is not (Zone.Edit or Zone.Done or Zone.Add or Zone.Reset or Zone.Layouts or Zone.Refresh or Zone.CatalogAdd or Zone.Sort or Zone.Info or Zone.Cols or Zone.Tile)) return;
        foreach (var (r, zone) in _zones)
            if (zone == _hot)
            {
                using var path = Theme.RoundRect(r, Sf(zone.Kind == Zone.CatalogAdd ? 6 : 8));
                using var pen = new Pen(Theme.Accent, 1.4f);
                g.DrawPath(pen, path);
                return;
            }
    }

    // ---- catalogue ------------------------------------------------------------------------
    /// <summary>The verified weekly summary, only while statistics are on (the badges are the return for sharing).</summary>
    private Stats.Summary? Summary => _d.Settings.StatsEnabled ? Stats.Current : null;

    private Font CatNameF => _ctx.Font(10f, FontStyle.Bold);
    private Font CatDescF => _ctx.Font(8.5f);
    private Font CatSmallF => _ctx.Font(8f, FontStyle.Bold);
    private int StarSize => Math.Max(S(15), CatDescF.Height);
    private int SortPillH => Math.Max(S(20), CatSmallF.Height + S(6));

    /// <summary>The description under a catalogue name: one or two lines, as the text wraps in this language and width.</summary>
    /// <summary>Unavailable, or how many of the type stand on the Dashboard (a type may stand any number of times), or what the widget is.</summary>
    private string CatDesc(DashWidgetDef def, bool available, int onBoard) =>
        !available ? Lang.T("dash_unavailable") : onBoard > 0 ? string.Format(Lang.T("dash_added_n"), onBoard) : Lang.T(def.NameKey + "_d");

    /// <summary>How many widgets of every type the layout holds.</summary>
    private Dictionary<string, int> PlacedCounts()
    {
        var n = new Dictionary<string, int>();
        foreach (var it in _items) n[it.Type] = n.TryGetValue(it.Type, out int c) ? c + 1 : 1;
        return n;
    }

    private int CatDescH(string desc, int w) =>
        Math.Min(CatDescF.Height * 2, TextRenderer.MeasureText(desc, CatDescF, new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height);

    /// <summary>Name line, the description, the stars line.</summary>
    private int CatalogRowH(int descH) => S(6) + CatNameF.Height + S(2) + descH + S(6) + StarSize + S(7);

    private int CatalogHeight(int width)
    {
        int head = _ctx.Font(11f, FontStyle.Bold).Height + S(10) + Math.Max(_ctx.Font(8.5f, FontStyle.Bold).Height, Summary != null ? SortPillH : 0) + S(8);
        int w = PanelW - S(16) * 2, rows = 0;
        var placed = PlacedCounts();
        foreach (var def in Listed)
            rows += CatalogRowH(CatDescH(CatDesc(def, def.Available(_d), placed.GetValueOrDefault(def.TypeId)), w));
        return S(16) * 2 + head + rows;
    }

    private Rectangle PanelRect() => new(Width - Pad - PanelW, GridTop(), PanelW, CatalogHeight(Width));

    /// <summary>The widget types the catalogue lists on this machine (a type can hide itself, e.g. the Community tile while statistics are off).</summary>
    private IEnumerable<DashWidgetDef> Listed => DashRegistry.All.Where(d => d.Listed?.Invoke(_d) ?? true);

    /// <summary>Catalogue order: by share of dashboards (Popular) or by name (A-Z) while a summary is in; the registry order otherwise.</summary>
    private List<DashWidgetDef> CatalogOrder(Stats.Summary? sum)
    {
        if (sum == null) return Listed.ToList();
        if (_sortPopular)
            return Listed.Select((d, i) => (d, i)).OrderByDescending(t => sum.ShareOf(t.d.TypeId) ?? -1).ThenBy(t => t.i).Select(t => t.d).ToList();
        return Listed.OrderBy(d => Lang.T(d.NameKey), StringComparer.Create(Lang.Culture, true)).ToList();
    }

    /// <summary>Five stars from the left of <paramref name="row"/>, the first <paramref name="filled"/> in <paramref name="fill"/>.</summary>
    private void PaintStars(Graphics g, Rectangle row, int filled, Color fill, Color empty, bool clear) =>
        DashRatings.PaintStars(g, row.Location, StarSize, filled, fill, empty, clear ? Theme.Card : null);

    private void PaintCatalog(Graphics g)
    {
        var panel = PanelRect();
        Ui.FillCard(g, panel);
        _starRows.Clear();
        var sum = Summary;
        int pad = S(16), x = panel.Left + pad, w = panel.Width - pad * 2, y = panel.Top + pad;
        var tf = _ctx.Font(11f, FontStyle.Bold);
        TextRenderer.DrawText(g, Lang.T("dash_add"), tf, new Rectangle(x, y, w, tf.Height), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        y += tf.Height + S(10);

        // section line: STANDARD on the left; with a summary the two sort pills on the right
        var secF = _ctx.Font(8.5f, FontStyle.Bold);
        int lineH = Math.Max(secF.Height, sum != null ? SortPillH : 0);
        TextRenderer.DrawText(g, Lang.Upper(Lang.T("dash_catalog_std")), secF, new Rectangle(x, y + (lineH - secF.Height) / 2, w, secF.Height), Theme.Faint, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        if (sum != null) PaintSortPills(g, x + w, y, lineH);
        y += lineH + S(8);

        var nameF = CatNameF;
        var descF = CatDescF;
        var btnF = _ctx.Font(9f, FontStyle.Bold);
        var placed = PlacedCounts();
        var ratings = _d.Settings.WidgetRatings;
        var order = CatalogOrder(sum);
        var popular = sum == null ? new HashSet<string>() : new HashSet<string>(sum.Top.Take(3));   // the badge: the three most placed
        string badge = Lang.Upper(Lang.T("dash_sort_popular"));
        for (int n = 0; n < order.Count; n++)
        {
            var def = order[n];
            bool available = def.Available(_d), onBoard = placed.ContainsKey(def.TypeId);
            string desc = CatDesc(def, available, placed.GetValueOrDefault(def.TypeId));
            int descH = CatDescH(desc, w), rowH = CatalogRowH(descH);
            var row = new Rectangle(x, y, w, rowH);
            var st = sum != null && sum.Widgets.TryGetValue(def.TypeId, out var ws) ? ws : null;

            // name line: the name, the POPULAR badge, the Add button at the right
            string btn = Lang.T("dash_add_short");
            int bw = TextRenderer.MeasureText(btn, btnF, Size.Empty, TextFormatFlags.NoPadding).Width + S(20), bh = Math.Max(S(26), btnF.Height + S(8));
            int nameTop = row.Top + S(6);
            var br = new Rectangle(row.Right - bw, nameTop + (nameF.Height - bh) / 2, bw, bh);
            int textW = w - bw - S(10);
            string name = Lang.T(def.NameKey);
            int nameW = Math.Min(textW, TextRenderer.MeasureText(name, nameF, Size.Empty, TextFormatFlags.NoPadding).Width);
            TextRenderer.DrawText(g, name, nameF, new Rectangle(x, nameTop, textW, nameF.Height), available ? Theme.Text : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            if (popular.Contains(def.TypeId))
            {
                int bwd = TextRenderer.MeasureText(badge, CatSmallF, Size.Empty, TextFormatFlags.NoPadding).Width + S(10), bht = CatSmallF.Height + S(4);
                if (x + nameW + S(8) + bwd <= x + textW)   // only when it fits beside the name
                {
                    var bdg = new Rectangle(x + nameW + S(8), nameTop + (nameF.Height - bht) / 2, bwd, bht);
                    using var path = Theme.RoundRect(bdg, bht / 2f);
                    using var b = new SolidBrush(Theme.AccentSoft);
                    g.FillPath(b, path);
                    TextRenderer.DrawText(g, badge, CatSmallF, bdg, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }

            // description: up to two lines across the whole width (the button sits on the name line)
            int descTop = nameTop + nameF.Height + S(2);
            TextRenderer.DrawText(g, desc, descF, new Rectangle(x, descTop, w, descH), onBoard && available ? Theme.Accent : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            // stars line: your rating (accent) or the average (muted), then "4.6 (23) · in 92 % of dashboards"
            int starsTop = descTop + descH + S(6);
            var starRow = new Rectangle(x, starsTop, StarSize * 5, StarSize);
            _starRows[def.TypeId] = starRow;
            int own = ratings.TryGetValue(def.TypeId, out int r0) ? r0 : 0;
            int avg = st?.Rating is { } a ? (int)Math.Round(a) : 0;
            PaintStars(g, starRow, own > 0 ? own : avg, own > 0 ? Theme.Accent : Theme.Muted, Theme.Faint, clear: false);
            for (int k = 1; k <= 5; k++) _zones.Add((new Rectangle(starRow.Left + (k - 1) * StarSize, starRow.Top, StarSize, StarSize), new Hot(Zone.Star, k, def.TypeId)));
            if (st != null)
            {
                string votes = st.Rating is { } av && st.Votes > 0 ? av.ToString("0.0", Lang.Culture) + " (" + st.Votes.ToString(Lang.Culture) + ")" : "";
                string share = string.Format(Lang.T("dash_share_fmt"), (int)Math.Round(st.Share * 100));
                int tx = starRow.Right + S(6), tw = row.Right - tx;
                // the longest form that fits: both, the share alone, the bare percentage (the stars already show the average)
                string tail = votes.Length > 0 ? votes + " · " + share : share;
                if (TextRenderer.MeasureText(tail, descF, Size.Empty, TextFormatFlags.NoPadding).Width > tw) tail = share;
                if (TextRenderer.MeasureText(tail, descF, Size.Empty, TextFormatFlags.NoPadding).Width > tw) tail = (int)Math.Round(st.Share * 100) + " %";
                TextRenderer.DrawText(g, tail, descF, new Rectangle(tx, starsTop + (StarSize - descF.Height) / 2, tw, descF.Height), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }

            if (available)   // also for a type already there: Add puts another one on the Dashboard
            {
                using (var path = Theme.RoundRect(br, Sf(6)))
                {
                    using var b = new SolidBrush(Theme.AccentFill);
                    g.FillPath(b, path);
                }
                TextRenderer.DrawText(g, btn, btnF, br, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                _zones.Add((br, new Hot(Zone.CatalogAdd, -1, def.TypeId)));
            }
            // the rest of the row opens the widget card (added last: the stars and the button win where they overlap)
            _zones.Add((new Rectangle(row.Left, row.Top, row.Width, row.Height - S(2)), new Hot(Zone.Info, -1, def.TypeId)));
            y += rowH;
            if (n < order.Count - 1)
                using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, x, y - 1, x + w, y - 1);
        }
    }

    /// <summary>
    /// A star clicked in the catalogue. With statistics off a card explains that ratings travel
    /// with the weekly report and offers to turn them on; the same star again clears the rating.
    /// </summary>
    private void Rate(string type, int stars) => DashRatings.Rate(_d, type, stars, FindForm(), Rebuild);

    /// <summary>
    /// The widget card for a type. From a tile's menu (<paramref name="of"/>) its picture is that
    /// tile, with its settings. From the catalogue it is the widget as it comes, and the card
    /// offers to add one (another one, when the type already stands on the Dashboard).
    /// </summary>
    internal GhostCardForm InfoCardFor(string type, DashItem? of = null)
    {
        var def = DashRegistry.Find(type) ?? throw new ArgumentException(type);
        int k = of == null ? -1 : _items.FindIndex(i => ReferenceEquals(i, of));
        var widget = k >= 0 ? _widgets[k] : Stock(def);
        bool canAdd = k < 0 && def.Available(_d);
        return new WidgetInfoCard(_d, def, widget, _ctx, Rebuild, canAdd ? () => AddType(type) : null);
    }

    private void OpenInfo(string type, DashItem? of = null)
    {
        if (DashRegistry.Find(type) == null) return;
        using var card = InfoCardFor(type, of);
        card.ShowOver(FindForm());
    }

    // One widget of every type with nothing set, for the catalogue's pictures and the widget
    // card: a placed one may look like anything since widgets have settings (1.45). Kept, so a
    // widget that loads something once (the fan curve's tables) does not load it on every paint.
    private readonly Dictionary<string, IDashWidget> _stock = new();
    private IDashWidget Stock(DashWidgetDef def) => _stock.TryGetValue(def.TypeId, out var w) ? w : _stock[def.TypeId] = def.New();

    /// <summary>The ⋯ of a tile: details and rating, the next size, remove, edit the layout.</summary>
    private void ShowTileMenu(int index, Point at) { if (TileMenuItems(index) is { } items) DashMenuPopup.Open(this, at, items); }

    /// <summary>Preview tool only: the tile menu of widget <paramref name="index"/> as a bitmap, second row lit.</summary>
    internal Bitmap? TileMenuPreview(int index) => TileMenuItems(index) is { } items ? DashMenuPopup.Render(this, items, 1) : null;

    /// <summary>The settings card of a placed widget (1.45), or null when the item is gone or the widget has nothing to set.</summary>
    internal GhostCardForm? SettingsCardFor(DashItem it)
    {
        int index = _items.FindIndex(i => ReferenceEquals(i, it));   // the layout may have changed while a menu was open
        if (index < 0 || DashRegistry.Find(it.Type) is not { } def) return null;
        var defs = _widgets[index].OptionDefs(_ctx);
        return defs.Count == 0 ? null : new WidgetSettingsCard(def, it, defs, _ctx, opt => ApplyOptions(it, opt), opt => FitsWith(it, opt));
    }

    private void OpenSettings(DashItem it)
    {
        using var card = SettingsCardFor(it);   // disposed on the way out: a modal form is not, by itself, and the card holds the widget's picture
        card?.ShowOver(FindForm());
    }

    /// <summary>
    /// Whether a widget with these settings still fits the Dashboard's area. Settings can change
    /// the sizes a widget allows (all profiles instead of one), and on a Dashboard that is full
    /// the larger size has no room: the card says so and stays open, nothing is cut.
    /// </summary>
    internal bool FitsWith(DashItem it, DashOptions opt)
    {
        var (w, h) = DashRegistry.FittedSize(it, opt);
        return w * h <= it.W * it.H || DashItem.Cells(_items) - it.W * it.H + w * h <= DashItem.MaxCells;
    }

    /// <summary>
    /// Save on the settings card: the settings go into the layout item, the item gets a new
    /// widget instance (an instance keeps its settings for life) and the Dashboard repaints once.
    /// </summary>
    internal void ApplyOptions(DashItem it, DashOptions opt)
    {
        if (!_items.Any(i => ReferenceEquals(i, it)) || opt.SameAs(DashOptions.Of(it)) || !FitsWith(it, opt)) return;
        // What is stored fits what one widget keeps, so the next start never has to cut it. When a
        // style from a file brought many keys this version does not use, those go first.
        int at = IndexOf(it);
        var own = at < 0 ? new HashSet<string>() : _widgets[at].OptionDefs(_ctx).SelectMany(d => new[] { d.Key, d.ColorKey }).Where(k => k.Length > 0).ToHashSet();
        opt.FitTo(DashItem.MaxOptKeys, DashItem.MaxOptChars, own);
        it.Opt = opt.ToMap();
        _byItem.Remove(it);
        Sync();   // a new instance, and with it the size: kept when the new settings still allow it, else the nearest allowed (one profile instead of four)
        Save();
    }

    private List<DashMenuPopup.Item>? TileMenuItems(int index)
    {
        if (index < 0 || index >= _items.Count) return null;
        var it = _items[index];
        string type = it.Type;
        // the size row names the size the click would give ("2×2 → 4×1"); a type with one size gets no row
        var sizes = _widgets[index].Sizes;
        int cur = -1;
        for (int i = 0; i < sizes.Count; i++) if (sizes[i].Width == it.W && sizes[i].Height == it.H) { cur = i; break; }
        var next = sizes.Count > 1 ? sizes[(cur + 1) % sizes.Count] : (Size?)null;
        var items = new List<DashMenuPopup.Item> { new(Lang.T("dash_menu_info"), () => OpenInfo(type, it)) };
        if (_widgets[index].OptionDefs(_ctx).Count > 0) items.Add(new(Lang.T("dash_menu_settings"), () => OpenSettings(it)));
        items.Add(DashMenuPopup.Item.Line);
        if (next is { } n) items.Add(new(string.Format(Lang.T("dash_menu_size_fmt"), $"{it.W}×{it.H} → {n.Width}×{n.Height}"), () => CycleSize(IndexOf(it))));
        if (DashRegistry.Find(type) != null) items.Add(new(Lang.T("dash_menu_duplicate"), () => Duplicate(it)));
        items.Add(new(Lang.T("dash_menu_remove"), () => RemoveAt(IndexOf(it)), Accent: Theme.Red));
        items.Add(DashMenuPopup.Item.Line);
        items.Add(new(Lang.T("dash_edit"), () => EditMode = true));
        return items;
    }

    /// <summary>
    /// Where a layout item stands now, -1 = gone (the layout may have changed while a menu was
    /// open). By the item itself, never by its type: a type may stand on the Dashboard many times.
    /// </summary>
    private int IndexOf(DashItem it) => _items.FindIndex(i => ReferenceEquals(i, it));

    /// <summary>Whether one more widget of this many cells still fits the Dashboard's bounds (the count and the area).</summary>
    private bool Room(int cells) => _items.Count < DashItem.MaxItems && DashItem.Cells(_items) + cells <= DashItem.MaxCells;

    /// <summary>A copy of a widget, with its size and its settings, right after the original; from there it goes its own way.</summary>
    internal void Duplicate(DashItem it)
    {
        int index = IndexOf(it);
        if (index < 0 || !Room(it.W * it.H)) return;
        _items.Insert(index + 1, it.Clone());
        Sync();
        Save();
    }

    // ---- actions ----------------------------------------------------------------------------
    internal bool EditMode
    {
        get => _edit;
        set
        {
            if (_edit == value) return;
            _edit = value;
            if (!_edit) { _catalog = false; _dragIndex = _pressIndex = -1; }
            _hot = new(Zone.None, -1, null);
            Replace();
            ContentChanged?.Invoke();
        }
    }

    internal bool CatalogOpen
    {
        get => _catalog;
        set { if (_catalog == value) return; _catalog = value; Replace(); ContentChanged?.Invoke(); }
    }

    /// <summary>The content height changed (edit mode, catalogue, layout edits): the page resizes the canvas.</summary>
    public event Action? ContentChanged;

    private void Save()
    {
        if (!_replacing) _undo = null;   // the layout changed by hand: the one before a load is no longer "the one before"
        try { _d.SaveSettings(); } catch { }
        Replace();
        ContentChanged?.Invoke();
    }

    internal void RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        _items.RemoveAt(index);
        Sync();
        Save();
    }

    internal void CycleSize(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        var sizes = _widgets[index].Sizes;
        if (sizes.Count == 0) return;
        var it = _items[index];
        int k = -1;
        for (int i = 0; i < sizes.Count; i++) if (sizes[i].Width == it.W && sizes[i].Height == it.H) { k = i; break; }
        var next = sizes[(k + 1) % sizes.Count];
        if (next.Width * next.Height > it.W * it.H && DashItem.Cells(_items) - it.W * it.H + next.Width * next.Height > DashItem.MaxCells) return;   // the larger size would pass the area's bound
        it.W = next.Width; it.H = next.Height;
        Save();
    }

    internal void AddType(string typeId)
    {
        var def = DashRegistry.Find(typeId);
        var size = def == null ? new Size(1, 1) : def.Sizes.Length > 0 ? def.Sizes[0] : new Size(1, 1);
        if (def == null || !Room(size.Width * size.Height)) return;
        _items.Add(new DashItem(typeId, size.Width, size.Height));
        Sync();
        Save();
    }

    internal void MoveItem(int from, int to)
    {
        if (from < 0 || from >= _items.Count || to < 0 || to >= _items.Count || from == to) return;
        var it = _items[from];
        var w = _widgets[from];
        _items.RemoveAt(from); _widgets.RemoveAt(from);
        _items.Insert(to, it); _widgets.Insert(to, w);
        Replace();
    }

    internal void ResetLayout() => ReplaceLayout(DashItem.DefaultLayout(), null);

    // ---- whole layouts: kept under a name, written to a file, read from one (1.45) -----------
    private (List<DashItem> items, int cols)? _undo;   // the layout before the last load, until the layout changes again
    private bool _replacing;

    /// <summary>
    /// Puts another layout in place of the current one (a saved one, one from a file, the default)
    /// and keeps the current one for one "undo". The items are copies: what comes in stays the
    /// caller's, what stands on the Dashboard is the Dashboard's.
    /// </summary>
    private void ReplaceLayout(IEnumerable<DashItem> items, int? cols)
    {
        var before = (_items.Select(i => i.Clone()).ToList(), _d.Settings.DashColumns);
        var incoming = items.Select(i => i.Clone()).ToList();
        DashItem.Sanitize(incoming);
        _items.Clear();
        _items.AddRange(incoming);
        if (cols is 0 or 4 or 5 or 6 or 8) _d.Settings.DashColumns = cols.Value;
        Sync();
        _replacing = true;
        try { Save(); } finally { _replacing = false; }
        _undo = before;
    }

    internal IReadOnlyList<DashSavedLayout> SavedLayouts => _d.Settings.DashboardSaved;
    internal bool CanUndoLayout => _undo != null;

    /// <summary>The layout from before the last load comes back; after that there is nothing to undo.</summary>
    internal void UndoLayout()
    {
        if (_undo is not { } u) return;
        ReplaceLayout(u.items, u.cols);
        _undo = null;
    }

    internal enum SaveAs { Done, NoName, Empty, Full }

    /// <summary>Whether the Dashboard has anything to keep or to write to a file.</summary>
    internal bool HasWidgets => _items.Count > 0;

    /// <summary>Keeps the current layout under a name (a name already there is replaced), or says why not.</summary>
    internal SaveAs SaveLayoutAs(string name)
    {
        name = DashOptions.Clean(name ?? "", DashSavedLayout.MaxName);
        if (name.Length == 0) return SaveAs.NoName;
        if (_items.Count == 0) return SaveAs.Empty;
        var list = _d.Settings.DashboardSaved;
        var entry = new DashSavedLayout { Name = name, Cols = _d.Settings.DashColumns, Items = _items.Select(i => i.Clone()).ToList() };
        int at = list.FindIndex(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) list[at] = entry;
        else if (list.Count >= DashSavedLayout.MaxLayouts) return SaveAs.Full;
        else list.Add(entry);
        try { _d.SaveSettings(); } catch { }
        return SaveAs.Done;
    }

    internal void LoadSavedLayout(int index)
    {
        var list = _d.Settings.DashboardSaved;
        if (index >= 0 && index < list.Count) ReplaceLayout(list[index].Items, list[index].Cols);
    }

    internal void DeleteSavedLayout(int index)
    {
        var list = _d.Settings.DashboardSaved;
        if (index < 0 || index >= list.Count) return;
        list.RemoveAt(index);
        try { _d.SaveSettings(); } catch { }
    }

    internal DashFile.Error ExportLayout(string path) => DashFile.Save(path, DashFile.Layout, _items, _d.Settings.DashColumns);

    /// <summary>Reads a layout file and, only when all of it checks out, puts it in place of the current layout.</summary>
    internal DashFile.Error ImportLayout(string path)
    {
        var c = DashFile.Read(path, out var err);
        if (c == null) return err;
        if (c.Kind != DashFile.Layout) return DashFile.Error.WrongKind;
        foreach (var it in c.Items) ScenesWidget.DropForeignPick(it, _d.Settings);
        // the area by the sizes the widgets would take here, not the ones the file states: a file
        // cannot write a widget smaller than it can be and get under the bound that way
        if (DashRegistry.FittedCells(c.Items) > DashItem.MaxCells) return DashFile.Error.TooMany;
        ReplaceLayout(c.Items, c.Cols);
        return DashFile.Error.None;
    }

    /// <summary>The Layouts card (also what the preview tool renders).</summary>
    internal GhostCardForm LayoutsCard() => new DashLayoutsCard(this);

    // ---- how often the Dashboard reads its data (1.45) ---------------------------------------
    internal static readonly int[] RefreshChoices = { 1500, 3000, 5000 };

    private void SetRefresh(int ms)
    {
        if (Array.IndexOf(RefreshChoices, ms) < 0 || _d.Settings.DashRefreshMs == ms) return;
        _d.Settings.DashRefreshMs = ms;
        try { _d.SaveSettings(); } catch { }
        Rebuild();
        ContentChanged?.Invoke();   // the page owns the timer and reads the setting from here
    }

    private void AskReset()
    {
        var owner = FindForm();
        var dlg = new GhostCardForm("//DASHBOARD", Lang.T("dash_reset"), Lang.T("dash_reset_q"),
            Lang.T("dash_reset"), Lang.T("gen_cancel"), ResetLayout);
        dlg.ShowOver(owner);
    }

    // ---- mouse ------------------------------------------------------------------------------
    private Hot HitZone(Point p)
    {
        foreach (var (r, zone) in _zones) if (r.Contains(p)) return zone;
        if (_view != View.Dashboard) return new Hot(Zone.None, -1, null);
        if (_edit && _rects.Length == _placed.Count)
            for (int i = 0; i < _rects.Length; i++)
            {
                if (!_rects[i].Contains(p)) continue;
                if (RemoveRect(_rects[i]).Contains(p)) return new Hot(Zone.Remove, i, null);
                if (SizeRect(_rects[i], i).Contains(p)) return new Hot(Zone.Size, i, null);
                return new Hot(Zone.Widget, i, null);
            }
        if (!_edit)
        {
            int k = HitWidget(p);
            if (k >= 0) return MoreRect(_rects[k]).Contains(p) ? new Hot(Zone.More, k, null) : new Hot(Zone.Widget, k, null);
        }
        return new Hot(Zone.None, -1, null);
    }

    // ---- the ⋯ of a tile (outside edit mode, shown while the cursor is on the tile) ----
    private int MoreBtn => S(26);
    private Rectangle MoreRect(Rectangle r) => new(r.Right - MoreBtn - S(8), r.Top + S(8), MoreBtn, MoreBtn);

    private void PaintMore(Graphics g, Rectangle tile, bool hot)
    {
        var r = MoreRect(tile);
        using (var path = Theme.RoundRect(r, Sf(6)))
        {
            using var b = new SolidBrush(hot ? Theme.AccentSoft : Color.FromArgb(230, Theme.Surface));
            g.FillPath(b, path);
            using var pen = new Pen(hot ? Theme.Accent : Theme.BorderStrong);
            g.DrawPath(pen, path);
        }
        using var dot = new SolidBrush(hot ? Theme.Accent : Theme.Muted);
        float d = Math.Max(2f, 2.4f * Sf(1)), cy = r.Top + r.Height / 2f - d / 2f, cx = r.Left + r.Width / 2f;
        foreach (float off in new[] { -1.9f, 0f, 1.9f }) g.FillEllipse(dot, cx + off * d - d / 2f, cy, d, d);
    }

    private int HitWidget(Point p)
    {
        if (_placed.Count == 0 || _rects.Length != _placed.Count) return -1;
        for (int i = 0; i < _rects.Length; i++) if (_rects[i].Contains(p)) return i;
        return -1;
    }

    private void SetHot(Hot h)
    {
        if (h == _hot) return;
        var old = _hot; _hot = h;
        foreach (var z in new[] { old, h })
        {
            if (z.Kind == Zone.None) continue;
            if (z.Kind is Zone.Remove or Zone.Size or Zone.Widget or Zone.More) { if (z.Index >= 0 && z.Index < _rects.Length) Invalidate(_rects[z.Index]); }
            else if (z.Kind == Zone.Star) { if (z.Type != null && _starRows.TryGetValue(z.Type, out var row)) Invalidate(Rectangle.Inflate(row, 2, 2)); }   // the whole row lights up to the star
            else if (z.Kind == Zone.Bar) Invalidate(Rectangle.Inflate(_chartBody, S(4), S(4)));   // the lit bar and its label
            else if (z.Kind == Zone.Row) { int li = z.Index / 1000; if (li >= 0 && li < _listBodies.Count) Invalidate(Rectangle.Inflate(_listBodies[li].body, S(10), S(4))); }
            else foreach (var (r, zone) in _zones) if (zone == z) Invalidate(Rectangle.Inflate(r, 2, 2));
        }
        if (_view == View.Stats && (IsSpotZone(old) || IsSpotZone(h))) { _mapHit = null; InvalidateSpot(); }   // the spotlight moves between the pointer's country and the walk's
    }

    /// <summary>
    /// A mouse message carries the position in 16 bits, so on a canvas taller than 32 767 px the
    /// position it gives wraps around. Such a canvas takes the cursor's position from the screen.
    /// </summary>
    private MouseEventArgs At(MouseEventArgs e)
    {
        if (Height < 32000) return e;
        var p = PointToClient(Cursor.Position);
        return new MouseEventArgs(e.Button, e.Clicks, p.X, p.Y, e.Delta);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        e = At(e);
        base.OnMouseMove(e);
        if (_dragIndex >= 0) { DragTo(e.Location); return; }
        if (_pressIndex >= 0 && e.Button == MouseButtons.Left)
        {
            var d = new Size(Math.Abs(e.X - _downAt.X), Math.Abs(e.Y - _downAt.Y));
            if (d.Width >= 4 || d.Height >= 4)   // the drag threshold of the cards
            {
                _dragIndex = _pressIndex; _pressIndex = -1;
                _dragOffset = new Size(_downAt.X - _rects[_dragIndex].Left, _downAt.Y - _rects[_dragIndex].Top);
                DragTo(e.Location);
            }
            return;
        }
        _mouse = e.Location;
        var h = HitZone(e.Location);
        if (h.Kind == Zone.Map && _hot.Kind == Zone.Map) { Invalidate(Rectangle.Inflate(_mapRect, S(220), S(60))); MapPointerMoved(); }   // the label follows the cursor
        SetHot(h);
        var want = h.Kind switch
        {
            Zone.None => Cursors.Default,
            Zone.Widget => _edit ? Cursors.SizeAll : Cursors.Hand,
            Zone.Map or Zone.Bar or Zone.Row or Zone.Tile => Cursors.Default,
            _ => Cursors.Hand,
        };
        if (Cursor != want) Cursor = want;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHot(new Hot(Zone.None, -1, null));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        e = At(e);
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _downAt = e.Location;
        var h = HitZone(e.Location);
        switch (h.Kind)
        {
            case Zone.Edit: EditMode = true; return;
            case Zone.Done: EditMode = false; return;
            case Zone.Add: CatalogOpen = !_catalog; return;
            case Zone.Reset: AskReset(); return;
            case Zone.Layouts: { using var card = LayoutsCard(); card.ShowOver(FindForm()); return; }
            case Zone.Refresh: SetRefresh(h.Index); return;
            case Zone.Remove: RemoveAt(h.Index); return;
            case Zone.Size: CycleSize(h.Index); return;
            case Zone.CatalogAdd: if (h.Type != null) AddType(h.Type); return;
            case Zone.Sort: _sortPopular = h.Index == 0; Rebuild(); return;
            case Zone.Star: if (h.Type != null) Rate(h.Type, h.Index); return;
            case Zone.More: ShowTileMenu(h.Index, new Point(MoreRect(_rects[h.Index]).Left, MoreRect(_rects[h.Index]).Bottom + S(4))); return;
            case Zone.Info: if (h.Type != null) OpenInfo(h.Type); return;
            case Zone.View: SwitchView((View)h.Index); return;
            case Zone.StatsOn: TurnStatsOn(); return;
            case Zone.StatsRefresh: _statsAskedAt = DateTime.Now; _d.StatsRefreshNow?.Invoke(); Rebuild(); return;   // SummaryChanged repaints when a newer file arrives
            case Zone.StatsInfo: ShowStatsInfo(); return;
            case Zone.Cols: SetColumns(h.Index); return;
            case Zone.Widget:
                if (_edit) _pressIndex = h.Index;
                else _placed[h.Index].Widget.Click(e.Location, _rects[h.Index], _ctx);
                return;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        e = At(e);
        base.OnMouseUp(e);
        _pressIndex = -1;
        if (_dragIndex >= 0)
        {
            _dragIndex = -1;
            Save();   // the order changed while dragging; the file follows the drop
        }
    }

    /// <summary>
    /// Drag preview: the dragged item takes the place of the widget under the cursor (before
    /// it in the left half, after it in the right half) and the layout re-flows at once, so
    /// the user sees where the card will land. The list is reordered in place and saved on drop.
    /// </summary>
    private void DragTo(Point p)
    {
        _dragPos = p;
        int over = HitWidget(p);
        int target = -1;
        if (over >= 0 && over != _dragIndex)
        {
            var r = _rects[over];
            bool before = p.X < r.Left + r.Width / 2;
            target = before ? over : over + 1;
            if (target > _dragIndex) target--;   // the dragged item leaves its slot first
        }
        else if (over < 0 && p.Y > (_rects.Length > 0 ? _rects.Max(r => r.Bottom) : 0)) target = _items.Count - 1;
        if (target >= 0 && target != _dragIndex)
        {
            MoveItem(_dragIndex, target);
            _dragIndex = target;
        }
        Invalidate();
    }

    /// <summary>Preview tool only: the rectangle of a zone so a bench can click it. kind: edit, done, add, reset, remove:i, size:i, catalog:typeId, widget:i.</summary>
    internal Rectangle ZoneRect(string spec)
    {
        EnsureStatic(VisibleRect());
        var rects = Rects();
        string kind = spec.Split(':')[0], arg = spec.Contains(':') ? spec[(spec.IndexOf(':') + 1)..] : "";
        Hot want = kind switch
        {
            "edit" => new Hot(Zone.Edit, -1, null),
            "done" => new Hot(Zone.Done, -1, null),
            "add" => new Hot(Zone.Add, -1, null),
            "reset" => new Hot(Zone.Reset, -1, null),
            "layouts" => new Hot(Zone.Layouts, -1, null),
            "catalog" => new Hot(Zone.CatalogAdd, -1, arg),
            "sort" => new Hot(Zone.Sort, int.TryParse(arg, out int si) ? si : 0, null),
            "view" => new Hot(Zone.View, int.TryParse(arg, out int vi) ? vi : 0, null),
            "cols" => new Hot(Zone.Cols, int.TryParse(arg, out int ci) ? ci : 0, null),
            "row" => new Hot(Zone.Row, int.TryParse(arg, out int ri) ? ri : 0, null),   // row:<list * 1000 + row> of the Statystyki lists
            "star" => arg.LastIndexOf(':') is var c and > 0 && int.TryParse(arg[(c + 1)..], out int sn) ? new Hot(Zone.Star, sn, arg[..c]) : new Hot(Zone.None, -1, null),   // star:typeId:n
            _ => new Hot(Zone.None, -1, null),
        };
        if (want.Kind != Zone.None) { foreach (var (r, zone) in _zones) if (zone == want) return r; return Rectangle.Empty; }
        int i = int.TryParse(arg, out int k) ? k : -1;
        if (i < 0 || i >= rects.Length) return Rectangle.Empty;
        return kind switch { "remove" => RemoveRect(rects[i]), "size" => SizeRect(rects[i], i), "widget" => rects[i], _ => Rectangle.Empty };
    }

    /// <summary>Preview tool only: the layout as "type:WxH,..." to print after an edit.</summary>
    internal string LayoutSpec() => string.Join(",", _items.Select(i => $"{i.Type}:{i.W}x{i.H}"));

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
}
