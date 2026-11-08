using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// The small menu of a Dashboard tile (the ⋯), in the look of the app's own popups: a dark
/// rounded card with a soft shadow, rows that light up under the cursor, thin separators. Built
/// like the themed drop-down list (ThemedComboBox.ListPopup): a borderless window that never
/// takes focus, closed by a click anywhere else, by Escape, or when the owner window moves or
/// loses focus. A row runs its action after the menu has gone, posted to the owner's queue.
/// (A ToolStripDropDown was tried first: its message filter closed and disposed the menu on
/// the press inside the hosted control, and the click then touched a disposed window.)
/// </summary>
internal sealed class DashMenuPopup : Form, IMessageFilter
{
    public sealed record Item(string Text, Action? Run, bool Separator = false, Color? Accent = null)
    {
        public static Item Line => new("", null, true);
    }

    private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080, CS_DROPSHADOW = 0x00020000;
    private const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
    private const int WM_KEYDOWN = 0x0100, WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_XBUTTONDOWN = 0x020B;
    private const int WM_NCLBUTTONDOWN = 0x00A1, WM_NCRBUTTONDOWN = 0x00A4, WM_NCMBUTTONDOWN = 0x00A7, VK_ESCAPE = 0x1B;

    private readonly IReadOnlyList<Item> _items;
    private readonly float _k;
    private readonly Font _font;
    private readonly int _rowH, _sepH, _padX, _padY;
    private Form? _owner;
    private Control? _anchor;
    private int _hot = -1;

    private DashMenuPopup(IReadOnlyList<Item> items, float k)
    {
        _items = items; _k = k;
        _font = Dpi.Font("Segoe UI", 10f);
        _rowH = Math.Max((int)(34 * k), _font.Height + (int)(14 * k));
        _sepH = (int)(9 * k);
        _padX = (int)(14 * k);
        _padY = (int)(6 * k);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Theme.Card;
        int w = (int)(180 * k);
        foreach (var it in items)
            if (!it.Separator) w = Math.Max(w, TextRenderer.MeasureText(it.Text, _font, Size.Empty, TextFormatFlags.NoPadding).Width + _padX * 2 + (int)(12 * k));
        int h = _padY * 2;
        foreach (var it in items) h += it.Separator ? _sepH : _rowH;
        ClientSize = new Size(w, h);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        using var path = Theme.RoundRect(new Rectangle(0, 0, Width, Height), 10 * _k);
        Region = new Region(path);
    }

    /// <summary>Opens the menu with its top-left corner at <paramref name="at"/> (client coordinates of <paramref name="anchor"/>), kept on screen.</summary>
    public static void Open(Control anchor, Point at, IReadOnlyList<Item> items)
    {
        var owner = anchor.FindForm();
        if (owner == null) return;
        var menu = new DashMenuPopup(items, Dpi.Of(anchor) / 96f);
        var screen = anchor.PointToScreen(at);
        var area = Screen.FromControl(anchor).WorkingArea;
        if (screen.X + menu.Width > area.Right) screen.X = area.Right - menu.Width;
        if (screen.Y + menu.Height > area.Bottom) screen.Y = Math.Max(area.Top, screen.Y - menu.Height - (int)(12 * Dpi.Of(anchor) / 96f));
        menu.Location = screen;
        menu._owner = owner;
        menu._anchor = anchor;
        owner.Deactivate += menu.OwnerChanged;
        owner.LocationChanged += menu.OwnerChanged;
        owner.SizeChanged += menu.OwnerChanged;
        owner.VisibleChanged += menu.OwnerChanged;
        Application.AddMessageFilter(menu);
        menu.Show(owner);
    }

    private void OwnerChanged(object? s, EventArgs e)
    {
        if (ReferenceEquals(Form.ActiveForm, this)) return;   // the owner lost focus to this very menu: not a reason to close
        Dismiss();
    }

    /// <summary>A press on the menu must not activate it (the owner would see Deactivate and the menu would close under the click).</summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
        base.WndProc(ref m);
    }

    private void Dismiss()
    {
        Unhook();
        if (!IsDisposed && !Disposing) { Hide(); Dispose(); }
    }

    private void Unhook()
    {
        Application.RemoveMessageFilter(this);
        if (_owner is not { } o) return;
        o.Deactivate -= OwnerChanged;
        o.LocationChanged -= OwnerChanged;
        o.SizeChanged -= OwnerChanged;
        o.VisibleChanged -= OwnerChanged;
        _owner = null;
    }

    protected override void OnFormClosed(FormClosedEventArgs e) { base.OnFormClosed(e); Unhook(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { Unhook(); _font.Dispose(); }
        base.Dispose(disposing);
    }

    /// <summary>A press anywhere but on this window, or Escape, closes the menu; the press itself goes on to its target.</summary>
    public bool PreFilterMessage(ref Message m)
    {
        if (IsDisposed) return false;
        switch (m.Msg)
        {
            case WM_LBUTTONDOWN: case WM_RBUTTONDOWN: case WM_MBUTTONDOWN: case WM_XBUTTONDOWN:
            case WM_NCLBUTTONDOWN: case WM_NCRBUTTONDOWN: case WM_NCMBUTTONDOWN:
                if (m.HWnd != Handle) Dismiss();
                return false;
            case WM_KEYDOWN:
                if ((int)m.WParam == VK_ESCAPE) { Dismiss(); return true; }
                return false;
        }
        return false;
    }

    // ---- rows ----
    private Rectangle RowRect(int index)
    {
        int y = _padY;
        for (int i = 0; i < index; i++) y += _items[i].Separator ? _sepH : _rowH;
        return new Rectangle(0, y, Width, _items[index].Separator ? _sepH : _rowH);
    }

    private int HitRow(Point p)
    {
        for (int i = 0; i < _items.Count; i++) if (!_items[i].Separator && RowRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e) => PaintMenu(e.Graphics, ClientSize, _items, _hot, _k, _font, _rowH, _sepH, _padX, _padY);

    private static void PaintMenu(Graphics g, Size size, IReadOnlyList<Item> items, int hot, float k, Font font, int rowH, int sepH, int padX, int padY)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Card);
        using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, size.Width - 1, size.Height - 1), 10 * k))
        using (var pen = new Pen(Theme.BorderStrong))
            g.DrawPath(pen, path);
        int y = padY;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            int h = it.Separator ? sepH : rowH;
            if (it.Separator)
            {
                using var sp = new Pen(Theme.Border);
                g.DrawLine(sp, padX, y + h / 2, size.Width - padX, y + h / 2);
            }
            else
            {
                bool lit = i == hot;
                if (lit)
                {
                    var hr = new Rectangle((int)(6 * k), y + (int)(2 * k), size.Width - (int)(12 * k), h - (int)(4 * k));
                    using var path = Theme.RoundRect(hr, 7 * k);
                    using var b = new SolidBrush(Theme.AccentSoft);
                    g.FillPath(b, path);
                }
                TextRenderer.DrawText(g, it.Text, font, new Rectangle(padX, y, size.Width - padX * 2, h), lit ? Theme.Accent : it.Accent ?? Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            y += h;
        }
    }

    /// <summary>Preview tool only: the menu as a bitmap, with row <paramref name="hot"/> under the cursor.</summary>
    internal static Bitmap Render(Control anchor, IReadOnlyList<Item> items, int hot)
    {
        using var menu = new DashMenuPopup(items, Dpi.Of(anchor) / 96f);
        var bmp = new Bitmap(menu.ClientSize.Width, menu.ClientSize.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var clip = Theme.RoundRect(new Rectangle(0, 0, bmp.Width, bmp.Height), 10 * menu._k);
            g.SetClip(clip);
            PaintMenu(g, menu.ClientSize, items, hot, menu._k, menu._font, menu._rowH, menu._sepH, menu._padX, menu._padY);
        }
        return bmp;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitRow(e.Location);
        if (h != _hot) { _hot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hot != -1) { _hot = -1; Invalidate(); }
    }

    private int _pressRow = -1;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) _pressRow = HitRow(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        int h = HitRow(e.Location);
        if (h < 0 || h != _pressRow || _items[h].Run is not { } run) return;   // press and release on the same row
        var anchor = _anchor;
        Dismiss();
        if (anchor is { IsDisposed: false }) anchor.BeginInvoke(run); else run();   // after this window is gone, on the owner's queue
    }
}
