using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// The change history as a table the app draws itself (Status → Change log): every entry ever
/// written (the in-app list plus the history files of all years, <see cref="ChangeLog.Everything"/>),
/// a search field that filters as you type, a time range, a click on a column header to sort,
/// and rows that light up under the mouse like the Models tab. The newest 200 rows show; "Show
/// 200 older" adds the next 200, so a long history never draws thousands of rows at once and
/// there are no pages to turn. Copy shown copies the rows on screen; Open history file opens
/// the year's file. The control reports its height to the page, which scrolls as usual.
/// </summary>
public sealed class HistoryView : Control
{
    private const int Step = 200;
    private static readonly Font Head = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold);
    private static readonly Font Cell = Dpi.Font("Segoe UI", 10.5f);
    private static readonly Font Mono = Dpi.Font("Consolas", 11f, FontStyle.Bold);
    private static readonly Font Note = Dpi.Font("Segoe UI", 9.5f);
    private static readonly Font Glyph = Dpi.Font("Segoe MDL2 Assets", 7.5f);
    private static readonly int[] RangeDays = { 1, 7, 30, 0 };   // 0 = everything

    private readonly SearchBox _search = new();
    private readonly SegControl _range;
    private readonly Button _older = new(), _copy = new(), _file = new();
    private List<LogEntry> _all = new();    // everything, newest first
    private List<LogEntry> _rows = new();   // after the range, the filter and the sort
    private string[][] _cells = Array.Empty<string[]>();   // the shown rows as text
    private int _shown = Step;
    private int _sortCol;                   // 0 time, 1 source, 2 detail, 3 readback
    private bool _sortAsc;                  // time: newest first by default
    private int _hover = -1, _hoverHead = -1;
    private int[] _left = new int[4], _width = new int[4];
    private int _headTop, _rowsTop, _rowH, _footTop, _height;
    private bool _dirty = true;

    /// <summary>Raised when the rows (and so the height) changed; the page lays itself out again.</summary>
    public event Action? HeightChanged;

    public HistoryView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _range = new SegControl(RangeLabels(), 3) { Size = new Size(300, 30) };   // dpi-ok: SegControl's MinimumSize grows it to its labels
        _range.SelectedChanged += _ => Refilter();
        _search.Box.TextChanged += (_, _) => Refilter();
        foreach (var b in new[] { _older, _copy, _file }) { b.AutoSize = true; b.Padding = new Padding(10, 4, 10, 4); Ui.StyleGhost(b); Controls.Add(b); }
        _older.Click += (_, _) => { _shown += Step; Relayout(); HeightChanged?.Invoke(); Invalidate(); };
        _copy.Click += (_, _) => Ui.CopyText(ShownText());
        _file.Click += (_, _) => OpenFile();
        Controls.Add(_search);
        Controls.Add(_range);
        SetLabels();
        ApplyTheme();
    }

    private int S(int v) => (int)Math.Ceiling(v * Dpi.Of(this) / 96f);
    private static string[] RangeLabels() => new[] { Lang.T("hist_today"), Lang.T("hist_7d"), Lang.T("hist_30d"), Lang.T("hist_all") };

    /// <summary>Captions follow a language change.</summary>
    public void SetLabels()
    {
        _range.SetItems(RangeLabels(), _range.Selected);
        _older.Text = Lang.T("hist_older");
        _copy.Text = Lang.T("hist_copy");
        _file.Text = Lang.T("log_history_file");
        Refilter();
    }

    public void ApplyTheme()
    {
        BackColor = Theme.Surface;
        _search.ApplyTheme();
        foreach (var b in new[] { _older, _copy, _file }) Ui.StyleGhost(b);
        Invalidate();
    }

    private readonly HashSet<(DateTime, ChangeSource, string)> _keys = new();

    /// <summary>
    /// Something was logged. On screen: the new entries are merged in from the in-app list (no file
    /// read, no re-parse); off screen: everything is read again at the next show.
    /// </summary>
    public void MarkDirty()
    {
        if (!Visible) { _dirty = true; return; }
        bool added = false;
        foreach (var e in ChangeLog.Recent(20))
        {
            var k = (e.Time, e.Source, e.Detail);
            if (_keys.Add(k)) { _all.Insert(0, e); added = true; }
        }
        if (added) Refilter();
    }

    /// <summary>The history files and the in-app list, read once per show (a few thousand lines take milliseconds).</summary>
    public void Reload()
    {
        _all = ChangeLog.Everything();
        _keys.Clear();
        foreach (var e in _all) _keys.Add((e.Time, e.Source, e.Detail));
        _dirty = false;
        Refilter();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && _dirty) Reload();
    }

    private void Refilter()
    {
        int days = RangeDays[Math.Clamp(_range.Selected, 0, RangeDays.Length - 1)];
        DateTime since = days == 0 ? DateTime.MinValue : DateTime.Now.Date.AddDays(1 - days);
        string q = _search.Box.Text.Trim();
        IEnumerable<LogEntry> rows = _all.Where(r => r.Time >= since);
        if (q.Length > 0)
            rows = rows.Where(r => Has(r.Time.ToString("yyyy-MM-dd HH:mm:ss"), q) || Has(ChangeLog.SourceLabel(r.Source), q) || Has(r.Detail, q) || Has(r.Result, q));
        rows = _sortCol switch
        {
            1 => _sortAsc ? rows.OrderBy(r => ChangeLog.SourceLabel(r.Source), StringComparer.CurrentCultureIgnoreCase).ThenByDescending(r => r.Time)
                          : rows.OrderByDescending(r => ChangeLog.SourceLabel(r.Source), StringComparer.CurrentCultureIgnoreCase).ThenByDescending(r => r.Time),
            2 => _sortAsc ? rows.OrderBy(r => r.Detail, StringComparer.CurrentCultureIgnoreCase).ThenByDescending(r => r.Time)
                          : rows.OrderByDescending(r => r.Detail, StringComparer.CurrentCultureIgnoreCase).ThenByDescending(r => r.Time),
            3 => _sortAsc ? rows.OrderBy(r => r.Result, StringComparer.Ordinal).ThenByDescending(r => r.Time)
                          : rows.OrderByDescending(r => r.Result, StringComparer.Ordinal).ThenByDescending(r => r.Time),
            _ => _sortAsc ? rows.OrderBy(r => r.Time) : rows.OrderByDescending(r => r.Time),
        };
        _rows = rows.ToList();
        _shown = Step;
        _hover = -1;
        Relayout();
        HeightChanged?.Invoke();
        Invalidate();
    }

    private static bool Has(string s, string q) => s.Contains(q, StringComparison.CurrentCultureIgnoreCase);

    private int Visible_() => Math.Min(_shown, _rows.Count);

    /// <summary>Height the control needs for what it shows; the page sizes it to this.</summary>
    public int PreferredHeight => _height;

    private void Relayout()
    {
        int w = Math.Max(S(300), Width);
        int top = 0;
        // search left, range right, on one row; the range goes under the search on a narrow page
        int searchH = _search.PreferredHeight;
        int rangeW = _range.Width, rangeH = _range.Height;
        int searchW = Math.Min(S(380), w - rangeW - S(16));
        if (searchW < S(200)) { searchW = Math.Min(S(380), w); _search.SetBounds(0, top, searchW, searchH); top += searchH + S(8); _range.Location = new Point(0, top); top += rangeH + S(12); }
        else { _search.SetBounds(0, top, searchW, searchH); _range.Location = new Point(w - rangeW, top + (searchH - rangeH) / 2); top += Math.Max(searchH, rangeH) + S(12); }

        _headTop = top;
        _rowH = Cell.Height + S(14);
        _rowsTop = _headTop + Head.Height + S(12);
        int n = Visible_();
        _cells = new string[n][];
        for (int i = 0; i < n; i++)
        {
            var e = _rows[i];
            _cells[i] = new[] { e.Time.ToString("yyyy-MM-dd HH:mm:ss"), ChangeLog.SourceLabel(e.Source), e.Detail, e.Result };
        }
        Columns(w);
        int bodyH = n == 0 ? Cell.Height + S(24) : n * _rowH;
        _footTop = _rowsTop + bodyH + S(12);
        // footer: the count on the left, the buttons on the right (Show older only while there is more)
        _older.Visible = _rows.Count > n;
        int bx = w;
        foreach (var b in new[] { _file, _copy, _older })
        {
            if (!b.Visible) continue;
            bx -= b.Width;
            b.Location = new Point(bx, _footTop);
            bx -= S(8);
        }
        int footH = Math.Max(_copy.Height, Note.Height);
        _height = _footTop + footH + S(8);
    }

    // Columns follow their content, as the change-log grid on Status did (#172): time, source and
    // readback get what their longest shown cell needs, the detail column takes the rest, and the
    // readback never takes more than 40 %.
    private void Columns(int w)
    {
        string[] heads = { Lang.T("log_col_time"), Lang.T("log_col_source"), Lang.T("log_col_detail"), Lang.T("log_col_result") };
        int gap = S(10), need0 = 0, need1 = 0, need3 = 0;
        need0 = TextRenderer.MeasureText(heads[0], Head).Width; need1 = TextRenderer.MeasureText(heads[1], Head).Width; need3 = TextRenderer.MeasureText(heads[3], Head).Width;
        foreach (var r in _cells)
        {
            need0 = Math.Max(need0, TextRenderer.MeasureText(r[0], Mono).Width);
            need1 = Math.Max(need1, TextRenderer.MeasureText(r[1], Cell).Width);
            need3 = Math.Max(need3, TextRenderer.MeasureText(r[3], Mono).Width);
        }
        int bar = S(14);   // room for the hover bar and the sort glyph
        int inner = w - bar;
        _width[0] = need0 + gap + S(8);
        _width[1] = need1 + gap + S(8);
        _width[3] = Math.Min(need3 + gap, inner * 2 / 5);
        _width[2] = Math.Max(S(120), inner - _width[0] - _width[1] - _width[3]);
        _left[0] = bar; for (int c = 1; c < 4; c++) _left[c] = _left[c - 1] + _width[c - 1];
    }

    private string ShownText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var r in _cells)
            sb.Append(r[0]).Append("  [").Append(r[1]).Append("]  ").Append(r[2]).Append(r[3].Length == 0 ? "" : "  ->  " + r[3]).AppendLine();
        return sb.ToString();
    }

    private static void OpenFile()
    {
        try
        {
            string p = ChangeLog.HistoryPath;
            if (!File.Exists(p)) File.WriteAllText(p, "");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p) { UseShellExecute = true });
        }
        catch (Exception ex) { AppLifecycle.Note("history file could not be opened: " + ex.Message); }
    }

    protected override void OnLayout(LayoutEventArgs levent) { base.OnLayout(levent); Relayout(); }

    private Rectangle RowRect(int i) => new(0, _rowsTop + i * _rowH, Width, _rowH);
    private Rectangle HeadRect(int c) => new(_left[c], _headTop, _width[c], Head.Height + S(8));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int row = -1, head = -1;
        if (e.Y >= _rowsTop && e.Y < _rowsTop + Visible_() * _rowH) row = (e.Y - _rowsTop) / _rowH;
        else if (e.Y >= _headTop && e.Y < _headTop + Head.Height + S(8)) for (int c = 0; c < 4; c++) if (HeadRect(c).Contains(e.Location)) head = c;
        if (row != _hover) { int old = _hover; _hover = row; if (old >= 0) Invalidate(RowRect(old)); if (row >= 0) Invalidate(RowRect(row)); }
        if (head != _hoverHead) { _hoverHead = head; Invalidate(new Rectangle(0, _headTop, Width, Head.Height + S(8))); }
        Cursor = head >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover >= 0) { int old = _hover; _hover = -1; Invalidate(RowRect(old)); }
        if (_hoverHead >= 0) { _hoverHead = -1; Invalidate(new Rectangle(0, _headTop, Width, Head.Height + S(8))); }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left || _hoverHead < 0) return;
        if (_sortCol == _hoverHead) _sortAsc = !_sortAsc;
        else { _sortCol = _hoverHead; _sortAsc = _hoverHead != 0; }   // text columns A→Z first, time newest first
        Refilter();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        string[] heads = { Lang.T("log_col_time"), Lang.T("log_col_source"), Lang.T("log_col_detail"), Lang.T("log_col_result") };
        int headH = Head.Height + S(8);
        // header: a flat band, the sorted column in accent with a small arrow
        using (var b = new SolidBrush(Theme.Card)) g.FillRectangle(b, 0, _headTop, Width, headH);
        using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, _headTop + headH - 1, Width, _headTop + headH - 1);
        for (int c = 0; c < 4; c++)
        {
            var r = HeadRect(c);
            var col = c == _sortCol ? Theme.Accent : c == _hoverHead ? Theme.Text : Theme.Muted;
            int arrowW = c == _sortCol ? S(14) : 0;
            TextRenderer.DrawText(g, heads[c], Head, new Rectangle(r.X + S(4), r.Y, Math.Max(10, r.Width - S(8) - arrowW), r.Height), col,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);   // dpi-ok: a header the content column sizes decide; the cells are data
            if (c == _sortCol)
            {
                int tw = Math.Min(r.Width - S(8) - arrowW, TextRenderer.MeasureText(heads[c], Head, Size.Empty, TextFormatFlags.NoPadding).Width);
                Ui.CenterGlyph(g, _sortAsc ? "" : "", Glyph, col, new RectangleF(r.X + S(4) + tw + S(2), r.Y, arrowW, r.Height));
            }
        }
        int n = Visible_();
        if (n == 0)
        {
            TextRenderer.DrawText(g, Lang.T("hist_none"), Cell, new Rectangle(0, _rowsTop, Width, Cell.Height + S(24)), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);   // dpi-ok: one centred line, the box is a font height plus air
        }
        var clip = e.ClipRectangle;
        for (int i = 0; i < n; i++)
        {
            var rr = RowRect(i);
            if (rr.Bottom < clip.Top || rr.Top > clip.Bottom) continue;
            if (i == _hover)
            {
                using (var b = new SolidBrush(Color.FromArgb(Theme.Dark ? 14 : 10, Theme.Text))) g.FillRectangle(b, rr);
                using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, rr.X, rr.Y, S(3), rr.Height);
            }
            using (var p = new Pen(Color.FromArgb(Theme.Dark ? 40 : 60, Theme.Border))) g.DrawLine(p, rr.X + S(14), rr.Bottom - 1, rr.Right, rr.Bottom - 1);
            var cells = _cells[i];
            for (int c = 0; c < 4; c++)
            {
                var font = c is 0 or 3 ? Mono : Cell;
                var col = c switch { 0 => Theme.Muted, 1 => Theme.Accent, 3 => Theme.Muted, _ => Theme.Text };
                TextRenderer.DrawText(g, cells[c], font, new Rectangle(_left[c] + S(4), rr.Y, Math.Max(10, _width[c] - S(8)), rr.Height), col,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);   // dpi-ok: cells are data in columns sized to their longest text; only the detail column may end in an ellipsis when the window is narrow
            }
        }
        // the count, left of the buttons
        string count = string.Format(Lang.T("hist_count"), n, _rows.Count);
        int countRight = Width;
        foreach (var b in new[] { _file, _copy, _older }) if (b.Visible) countRight = Math.Min(countRight, b.Left);
        TextRenderer.DrawText(g, count, Note, new Rectangle(0, _footTop, Math.Max(10, countRight - S(12)), Math.Max(_copy.Height, Note.Height)), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);   // dpi-ok: a short count that yields to the buttons on a narrow page
    }
}
