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
internal sealed class DashCanvas : Control
{
    private readonly MainDeps _d;
    private readonly DashContext _ctx;

    // the static layer and the key it was rendered for
    private BufferedGraphics? _static;
    private int _sW, _sH, _sDpi;
    private bool _sDark, _sGrid, _staticDirty = true;
    private string _sLang = "";

    // the layout: the items (the settings list itself, so edits are in place), one widget per
    // item, their placement for the current column count and the pixel rectangles
    private List<DashItem> _items = new();
    private readonly List<IDashWidget> _widgets = new();
    private List<DashPlaced> _placed = new();
    private Rectangle[] _rects = Array.Empty<Rectangle>();
    private int _placedCols = -1, _rectsCell = -1, _rectsTop = -1, _rectsGridW = -1;

    // edit mode
    private bool _edit, _catalog;
    private enum Zone { None, Edit, Done, Add, Reset, Remove, Size, CatalogAdd, Widget }
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
        _ctx = new DashContext(d, this) { RequestRebuild = Rebuild };
        BackColor = Theme.Surface;
        ResizeRedraw = true;
        // OptimizedDoubleBuffer: OnPaint composes the static blit and the live layer offscreen
        // and presents once, so a tick never shows the static layer without its needles.
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
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
        return Math.Clamp((avail + Gap) / (MinCell + Gap), MinColumns, MaxColumns);
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
        return S(24) + Math.Max(f.Height, ButtonH) + S(28);
    }

    /// <summary>Height the page sizes this control to (the page scrolls it when taller than the window).</summary>
    public int ContentHeight(int width)
    {
        Sync();
        var placed = Placement(width);
        int rows = Math.Max(1, DashLayout.Rows(placed)), cell = Cell(width);
        int grid = GridTop() + rows * cell + (rows - 1) * Gap + Pad;
        int panel = _catalog ? GridTop() + CatalogHeight(width) + Pad : 0;
        return Math.Max(grid, panel);
    }

    // ---- layout -----------------------------------------------------------------------
    /// <summary>
    /// Re-reads the layout from the settings. Widget instances are kept across reorders,
    /// removals and additions (matched by type), so a curve widget keeps its tables and a ring
    /// keeps what it last painted; only new types get new instances.
    /// </summary>
    public void Sync()
    {
        var items = DashLayout.ItemsOf(_d.Settings);
        bool same = ReferenceEquals(items, _items) && items.Count == _widgets.Count;
        if (same)
            for (int i = 0; i < items.Count; i++)
                if (items[i].Type != _widgets[i].TypeId) { same = false; break; }
        if (same) return;
        var pool = new List<IDashWidget>(_widgets);
        _widgets.Clear();
        foreach (var it in items)
        {
            int k = pool.FindIndex(w => w.TypeId == it.Type);
            if (k >= 0) { _widgets.Add(pool[k]); pool.RemoveAt(k); }
            else _widgets.Add(DashRegistry.Make(it));
        }
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
        Sync();
        var rects = Rects();
        _zones.Clear();
        PaintHeader(g);
        for (int i = 0; i < _placed.Count; i++) _placed[i].Widget.PaintStatic(g, rects[i], _ctx);
        if (_placed.Count == 0)
        {
            int gw = GridWidth(Width);
            DashPaint.Note(g, new Rectangle(Pad, GridTop(), gw - Pad * 2, Math.Max(S(40), Cell(Width))), _ctx, Lang.T("dash_empty"));
        }
        if (_catalog) PaintCatalog(g);
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
        if (_edit) PaintEditChrome(e.Graphics, rects, clip);
        PaintHot(e.Graphics);
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

    // ---- header -------------------------------------------------------------------------
    private void PaintHeader(Graphics g)
    {
        using var tf = TitleFont();
        int bandH = Math.Max(tf.Height, ButtonH), top = S(24);
        TextRenderer.DrawText(g, Lang.T("tab_dashboard"), tf, new Rectangle(Pad, top, Width / 2, bandH), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        int x = GridWidth(Width) - Pad;
        if (_edit)
        {
            x = HeaderButton(g, x, top, bandH, "", Lang.T("dash_done"), new Hot(Zone.Done, -1, null), primary: true);
            x = HeaderButton(g, x - S(8), top, bandH, "", Lang.T("dash_reset"), new Hot(Zone.Reset, -1, null), primary: false);
            HeaderButton(g, x - S(8), top, bandH, "", Lang.T("dash_add"), new Hot(Zone.Add, -1, null), primary: false, active: _catalog);
        }
        else HeaderButton(g, x, top, bandH, "", Lang.T("dash_edit"), new Hot(Zone.Edit, -1, null), primary: false);
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
        if (_hot.Kind is not (Zone.Edit or Zone.Done or Zone.Add or Zone.Reset or Zone.CatalogAdd)) return;
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
    private int CatalogRowH => Math.Max(S(56), _ctx.Font(10f, FontStyle.Bold).Height + _ctx.Font(8.5f).Height * 2 + S(14));

    private int CatalogHeight(int width)
    {
        int head = _ctx.Font(11f, FontStyle.Bold).Height + S(10) + _ctx.Font(8.5f, FontStyle.Bold).Height + S(8);
        return S(16) * 2 + head + DashRegistry.All.Count * CatalogRowH;
    }

    private Rectangle PanelRect() => new(Width - Pad - PanelW, GridTop(), PanelW, CatalogHeight(Width));

    private void PaintCatalog(Graphics g)
    {
        var panel = PanelRect();
        Ui.FillCard(g, panel);
        int pad = S(16), x = panel.Left + pad, w = panel.Width - pad * 2, y = panel.Top + pad;
        var tf = _ctx.Font(11f, FontStyle.Bold);
        TextRenderer.DrawText(g, Lang.T("dash_add"), tf, new Rectangle(x, y, w, tf.Height), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        y += tf.Height + S(10);
        var secF = _ctx.Font(8.5f, FontStyle.Bold);
        TextRenderer.DrawText(g, Lang.Upper(Lang.T("dash_catalog_std")), secF, new Rectangle(x, y, w, secF.Height), Theme.Faint, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        y += secF.Height + S(8);
        var nameF = _ctx.Font(10f, FontStyle.Bold);
        var descF = _ctx.Font(8.5f);
        var btnF = _ctx.Font(9f, FontStyle.Bold);
        int rowH = CatalogRowH;
        var present = new HashSet<string>(_items.Select(i => i.Type));
        foreach (var def in DashRegistry.All)
        {
            var row = new Rectangle(x, y, w, rowH);
            bool available = def.Available(_d), onBoard = present.Contains(def.TypeId);
            string btn = Lang.T("dash_add_short");
            int bw = TextRenderer.MeasureText(btn, btnF, Size.Empty, TextFormatFlags.NoPadding).Width + S(20), bh = Math.Max(S(26), btnF.Height + S(8));
            var br = new Rectangle(row.Right - bw, row.Top + (rowH - bh) / 2, bw, bh);
            int textW = w - bw - S(10);
            TextRenderer.DrawText(g, Lang.T(def.NameKey), nameF, new Rectangle(x, row.Top + S(6), textW, nameF.Height), available ? Theme.Text : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            string desc = !available ? Lang.T("dash_unavailable") : onBoard ? Lang.T("dash_added") : Lang.T(def.NameKey + "_d");
            TextRenderer.DrawText(g, desc, descF, new Rectangle(x, row.Top + S(6) + nameF.Height + S(2), textW, descF.Height * 2), onBoard && available ? Theme.Accent : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            if (available && !onBoard)
            {
                using (var path = Theme.RoundRect(br, Sf(6)))
                {
                    using var b = new SolidBrush(Theme.AccentFill);
                    g.FillPath(b, path);
                }
                TextRenderer.DrawText(g, btn, btnF, br, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                _zones.Add((br, new Hot(Zone.CatalogAdd, -1, def.TypeId)));
            }
            y += rowH;
            if (def != DashRegistry.All[^1])
                using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, x, y - 1, x + w, y - 1);
        }
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
        it.W = next.Width; it.H = next.Height;
        Save();
    }

    internal void AddType(string typeId)
    {
        var def = DashRegistry.Find(typeId);
        if (def == null || _items.Any(i => i.Type == typeId)) return;
        var size = def.Sizes.Length > 0 ? def.Sizes[0] : new Size(1, 1);
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

    internal void ResetLayout()
    {
        _items.Clear();
        _items.AddRange(DashItem.DefaultLayout());
        Sync();
        Save();
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
        if (_edit && _rects.Length == _placed.Count)
            for (int i = 0; i < _rects.Length; i++)
            {
                if (!_rects[i].Contains(p)) continue;
                if (RemoveRect(_rects[i]).Contains(p)) return new Hot(Zone.Remove, i, null);
                if (SizeRect(_rects[i], i).Contains(p)) return new Hot(Zone.Size, i, null);
                return new Hot(Zone.Widget, i, null);
            }
        if (!_edit) { int k = HitWidget(p); if (k >= 0) return new Hot(Zone.Widget, k, null); }
        return new Hot(Zone.None, -1, null);
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
            if (z.Kind is Zone.Remove or Zone.Size or Zone.Widget) { if (z.Index >= 0 && z.Index < _rects.Length) Invalidate(_rects[z.Index]); }
            else foreach (var (r, zone) in _zones) if (zone == z) Invalidate(Rectangle.Inflate(r, 2, 2));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
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
        var h = HitZone(e.Location);
        SetHot(h);
        var want = h.Kind switch
        {
            Zone.None => Cursors.Default,
            Zone.Widget => _edit ? Cursors.SizeAll : Cursors.Hand,
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
            case Zone.Remove: RemoveAt(h.Index); return;
            case Zone.Size: CycleSize(h.Index); return;
            case Zone.CatalogAdd: if (h.Type != null) AddType(h.Type); return;
            case Zone.Widget:
                if (_edit) _pressIndex = h.Index;
                else _placed[h.Index].Widget.Click(e.Location, _rects[h.Index], _ctx);
                return;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
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
        EnsureStatic();
        var rects = Rects();
        string kind = spec.Split(':')[0], arg = spec.Contains(':') ? spec[(spec.IndexOf(':') + 1)..] : "";
        Hot want = kind switch
        {
            "edit" => new Hot(Zone.Edit, -1, null),
            "done" => new Hot(Zone.Done, -1, null),
            "add" => new Hot(Zone.Add, -1, null),
            "reset" => new Hot(Zone.Reset, -1, null),
            "catalog" => new Hot(Zone.CatalogAdd, -1, arg),
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
