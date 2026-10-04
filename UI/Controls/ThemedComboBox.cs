using System.Collections;
using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// The app's drop-down list, drawn entirely by the app: the closed field is this control, the
/// open list is a small borderless window of its own (<see cref="ListPopup"/>). Colours are read
/// from <see cref="Theme"/> at paint time, so a light/dark switch only needs Invalidate.
///
/// It is deliberately not the system COMBOBOX. That control keeps a white field, button and list
/// in dark mode, and it is expensive where other programs listen for window events: creating one
/// and inserting its items raises an event per window and per item, which Windows hands to every
/// listener. Measured on a desktop with several of them: ~45 ms per list against ~1 ms for a
/// plain control, with more than twenty lists on the Settings page (docs/RENDERING.md §5).
///
/// The members mirror the ComboBox ones the app uses (Items, SelectedIndex, SelectedItem,
/// SelectedIndexChanged, DroppedDown, BeginUpdate/EndUpdate), and the height is fixed to
/// ItemHeight + 6 like the system control, so layouts written for it stay as they are.
/// Keyboard: arrows / Home / End / PgUp / PgDn change the value, a letter jumps to the next item
/// starting with it, F4 or Alt+Down opens the list; in the open list Enter picks, Esc cancels.
/// The mouse wheel never changes the value: closed, it scrolls the page; open, it scrolls the list.
/// </summary>
public sealed class ThemedComboBox : Control
{
    /// <summary>The items, in the shape of ComboBox.ObjectCollection.</summary>
    public sealed class ItemList : IEnumerable
    {
        private readonly ThemedComboBox _owner;
        private readonly List<object> _list = new();
        internal ItemList(ThemedComboBox owner) => _owner = owner;

        public int Count => _list.Count;
        public object this[int index]
        {
            get => _list[index];
            set { _list[index] = value; _owner.ItemsChanged(); }
        }
        public int Add(object item) { _list.Add(item); _owner.ItemsChanged(); return _list.Count - 1; }
        public void AddRange(object[] items) { _list.AddRange(items); _owner.ItemsChanged(); }
        public void Insert(int index, object item)
        {
            _list.Insert(index, item);
            if (index <= _owner._sel) _owner._sel++;
            _owner.ItemsChanged();
        }
        public void RemoveAt(int index)
        {
            _list.RemoveAt(index);
            if (index == _owner._sel) _owner._sel = -1;
            else if (index < _owner._sel) _owner._sel--;
            _owner.ItemsChanged();
        }
        public void Remove(object item) { int i = _list.IndexOf(item); if (i >= 0) RemoveAt(i); }
        /// <summary>Empties the list; the selection goes with it, without raising SelectedIndexChanged.</summary>
        public void Clear() { _list.Clear(); _owner._sel = -1; _owner.ItemsChanged(); }
        public int IndexOf(object item) => _list.IndexOf(item);
        public bool Contains(object item) => _list.Contains(item);
        public IEnumerator GetEnumerator() => _list.GetEnumerator();
    }

    private readonly ItemList _items;
    private int _sel = -1;
    private int _itemHeight = 30;   // roomy rows in the drop-down list
    private int _updating;
    private ListPopup? _popup;

    public ThemedComboBox()
    {
        _items = new ItemList(this);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        AccessibleRole = AccessibleRole.ComboBox;
        TabStop = true;
        Size = new Size(121, _itemHeight + 6);
    }

    public ItemList Items => _items;
    public event EventHandler? SelectedIndexChanged;
    /// <summary>True while the list is open.</summary>
    public bool DroppedDown => _popup != null;
    /// <summary>Rows shown before the list scrolls.</summary>
    public int MaxDropDownItems { get; set; } = 8;
    /// <summary>Width of the open list; anything below the field's width means "as wide as the field".</summary>
    public int DropDownWidth { get; set; }

    /// <summary>Height of one row of the open list; the field is this plus 6.</summary>
    public int ItemHeight
    {
        get => _itemHeight;
        set { _itemHeight = Math.Max(12, value); Height = _itemHeight + 6; }
    }

    public int SelectedIndex
    {
        get => _sel;
        set
        {
            int v = Math.Clamp(value, -1, _items.Count - 1);
            if (v == _sel) return;
            _sel = v;
            Invalidate();
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object? SelectedItem
    {
        get => _sel >= 0 && _sel < _items.Count ? _items[_sel] : null;
        set { if (value != null) { int i = _items.IndexOf(value); if (i >= 0) SelectedIndex = i; } }
    }

    /// <summary>The selected item as text. Assigning selects the item with exactly that text, if there is one.</summary>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => SelectedItem?.ToString() ?? "";
        set
        {
            for (int i = 0; value != null && i < _items.Count; i++)
                if (_items[i]?.ToString() == value) { SelectedIndex = i; return; }
        }
    }

    /// <summary>Several item changes, one repaint.</summary>
    public void BeginUpdate() => _updating++;
    public void EndUpdate() { if (_updating > 0 && --_updating == 0) Invalidate(); }

    private void ItemsChanged()
    {
        if (_popup != null) CloseList(false);   // the open list shows a snapshot of the items
        if (_updating == 0) Invalidate();
    }

    // The system control fixes its own height; layouts written for it rely on that.
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified) =>
        base.SetBoundsCore(x, y, width, _itemHeight + 6, specified | BoundsSpecified.Height);

    // ---------------- the closed field ----------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.Surface)) g.FillRectangle(bg, ClientRectangle);
        var textRect = new Rectangle(8, 0, Width - 30, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? Theme.Text : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        int cx = Width - 13, cy = Height / 2;
        using (var pen = new Pen(Theme.Muted, 1.6f))
            g.DrawLines(pen, new[] { new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2) });
        // keyboard focus shows on the frame; a mouse click does not light it up
        using (var bp = new Pen(Focused && ShowFocusCues ? Theme.Accent : Theme.Border))
            g.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); CloseList(false); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (!Enabled) CloseList(false); Invalidate(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) CloseList(false); }
    protected override void OnHandleDestroyed(EventArgs e) { CloseList(false); base.OnHandleDestroyed(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        OpenList();   // a click on the field of an OPEN list never gets here: the list's filter closes it and eats the click
    }

    // Windows routes the wheel to the control under the cursor, and a system combo answers by
    // changing its value - scrolling Settings past the language list switched the language
    // (discussion #9). Here the wheel never changes the value: with the list open it scrolls
    // the list; closed, the event stays unhandled and Windows passes it on to the page.
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_popup != null)
        {
            _popup.Wheel(e.Delta);
            if (e is HandledMouseEventArgs h) h.Handled = true;
            return;
        }
        base.OnMouseWheel(e);
    }

    // ---------------- keyboard ----------------

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
    {
        Keys.Up or Keys.Down or Keys.Left or Keys.Right => true,
        _ => base.IsInputKey(keyData),
    };

    // Enter / Esc / Tab of an open list are taken here, before the window's own shortcuts see them.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_popup != null)
        {
            switch (keyData)
            {
                case Keys.Escape: CloseList(false); return true;
                case Keys.Enter: CloseList(true); return true;
                case Keys.Tab: case Keys.Tab | Keys.Shift:
                    CloseList(true);
                    if (IsDisposed) return true;   // the pick rebuilt the page this control lived on
                    break;                         // then let the focus move on
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if ((e.KeyCode == Keys.F4 && !e.Alt) || (e.Alt && e.KeyCode is Keys.Down or Keys.Up))
        {
            if (_popup != null) CloseList(true); else OpenList();
            e.Handled = true;
            return;
        }
        int count = _items.Count;
        if (count == 0 || e.Alt || e.Control) return;
        int cur = _popup?.Hot ?? _sel, page = Math.Max(1, MaxDropDownItems - 1);
        int next = e.KeyCode switch
        {
            Keys.Up or Keys.Left => cur - 1,
            Keys.Down or Keys.Right => cur + 1,
            Keys.PageUp => cur - page,
            Keys.PageDown => cur + page,
            Keys.Home => 0,
            Keys.End => count - 1,
            _ => int.MinValue,
        };
        if (next == int.MinValue) return;
        e.Handled = true;
        Step(Math.Clamp(next, 0, count - 1));
    }

    // A letter jumps to the next item that starts with it.
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (e.Handled || char.IsControl(e.KeyChar) || _items.Count == 0) return;
        int cur = _popup?.Hot ?? _sel;
        for (int step = 1; step <= _items.Count; step++)
        {
            int i = (Math.Max(cur, -1) + step) % _items.Count;
            string t = _items[i]?.ToString() ?? "";
            if (t.Length == 0 || char.ToUpperInvariant(t[0]) != char.ToUpperInvariant(e.KeyChar)) continue;
            e.Handled = true;
            Step(i);
            return;
        }
    }

    // Open list: move the highlight (Enter picks). Closed: change the value right away.
    private void Step(int index)
    {
        if (_popup != null) _popup.SetHot(index);
        else SelectedIndex = index;
    }

    // ---------------- the open list ----------------

    /// <summary>Opens the list under the field (above it when there is no room below).</summary>
    public void OpenList()
    {
        if (_popup != null || _items.Count == 0 || !Enabled || !IsHandleCreated || FindForm() is not { } owner) return;
        _popup = new ListPopup(this);
        _popup.ShowFor(owner);
    }

    /// <summary>Closes the list; with <paramref name="commit"/> the highlighted row becomes the value.</summary>
    private void CloseList(bool commit)
    {
        if (_popup is not { } p) return;
        int hot = p.Hot;
        _popup = null;
        p.Dismiss();
        if (commit && hot >= 0 && !IsDisposed) SelectedIndex = hot;
    }

    /// <summary>
    /// The list window. It never takes activation or focus (the app window stays active and the
    /// keys keep going to the field), so it cannot rely on Deactivate to know when to go away:
    /// a message filter closes it on any mouse press outside it, and the owner window closes it
    /// when it moves, resizes, hides or loses activation.
    /// </summary>
    private sealed class ListPopup : Form, IMessageFilter
    {
        private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080, CS_DROPSHADOW = 0x00020000;
        private const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3, WM_MOUSEWHEEL = 0x020A;
        private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_XBUTTONDOWN = 0x020B;
        private const int WM_NCLBUTTONDOWN = 0x00A1, WM_NCRBUTTONDOWN = 0x00A4, WM_NCMBUTTONDOWN = 0x00A7;
        private const int BarW = 10;   // scroll strip on the right, only when the list scrolls

        private readonly ThemedComboBox _field;
        private Form? _owner;
        private int _top;              // index of the first visible row
        private int _rows;             // visible rows
        private bool _dragging;        // the scroll thumb is being dragged
        private int _dragOffset;       // cursor offset inside the thumb at the start of the drag

        /// <summary>The highlighted row: starts on the current value, follows the mouse and the arrows.</summary>
        public int Hot { get; private set; }

        public ListPopup(ThemedComboBox field)
        {
            _field = field;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Font = field.Font;
            Hot = field._sel;
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

        private int Count => _field._items.Count;
        private int RowH => _field._itemHeight;
        private bool Scrolls => Count > _rows;

        public void ShowFor(Form owner)
        {
            _rows = Math.Clamp(Count, 1, Math.Max(1, _field.MaxDropDownItems));
            int w = Math.Max(_field.Width, _field.DropDownWidth), h = _rows * RowH + 2;
            var below = _field.PointToScreen(new Point(0, _field.Height));
            var area = Screen.FromControl(_field).WorkingArea;
            int y = below.Y + h <= area.Bottom ? below.Y : below.Y - _field.Height - h;
            Bounds = new Rectangle(Math.Max(area.Left, Math.Min(below.X, area.Right - w)), y, w, h);
            Reveal(Hot);
            _owner = owner;
            owner.Deactivate += OwnerChanged;
            owner.LocationChanged += OwnerChanged;
            owner.SizeChanged += OwnerChanged;
            owner.VisibleChanged += OwnerChanged;
            Application.AddMessageFilter(this);
            Show(owner);
        }

        public void Dismiss()
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

        // Closed from outside (the owner window going away takes its owned windows with it):
        // the field must not keep pointing at a dead list, and the filter must not outlive it.
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Unhook();
            if (ReferenceEquals(_field._popup, this)) _field._popup = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Unhook();
            base.Dispose(disposing);
        }

        private void OwnerChanged(object? s, EventArgs e) => _field.CloseList(false);

        public bool PreFilterMessage(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_LBUTTONDOWN: case WM_RBUTTONDOWN: case WM_MBUTTONDOWN: case WM_XBUTTONDOWN:
                case WM_NCLBUTTONDOWN: case WM_NCRBUTTONDOWN: case WM_NCMBUTTONDOWN:
                    if (IsHandleCreated && m.HWnd == Handle) return false;
                    bool onField = _field.IsHandleCreated && m.HWnd == _field.Handle;
                    _field.CloseList(false);
                    return onField;   // a press on the field only closes the list; anywhere else it also does its own job
                case WM_MOUSEWHEEL:
                    Wheel((short)((long)m.WParam >> 16));
                    return true;      // while the list is open the wheel belongs to it, wherever the cursor is
            }
            return false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        /// <summary>Highlights a row and scrolls it into view (keyboard).</summary>
        public void SetHot(int index)
        {
            Hot = Math.Clamp(index, 0, Count - 1);
            Reveal(Hot);
            Invalidate();
        }

        /// <summary>One wheel notch: the system's lines-per-notch, or a whole page when Windows is set to scroll by screens.</summary>
        public void Wheel(int delta)
        {
            int lines = SystemInformation.MouseWheelScrollLines;
            ScrollBy(-Math.Sign(delta) * (lines > 0 ? lines : _rows));
        }

        private void ScrollBy(int rows)
        {
            if (!Scrolls) return;
            int top = Math.Clamp(_top + rows, 0, Count - _rows);
            if (top == _top) return;
            _top = top;
            var p = PointToClient(Cursor.Position);
            if (ClientRectangle.Contains(p) && p.X < Width - BarW) Hot = RowAt(p.Y);   // the highlight stays under the cursor
            Invalidate();
        }

        private void Reveal(int index)
        {
            if (index < 0) return;
            if (index < _top) _top = index;
            else if (index >= _top + _rows) _top = index - _rows + 1;
            _top = Math.Clamp(_top, 0, Math.Max(0, Count - _rows));
        }

        private int RowAt(int y) => Math.Clamp(_top + (y - 1) / RowH, 0, Count - 1);

        // scroll strip geometry: the thumb's height and top, inside the 1 px frame
        private (int Track, int Thumb, int Y) Bar()
        {
            int track = Height - 2, thumb = Math.Max(20, track * _rows / Count);
            return (track, thumb, 1 + (track - thumb) * _top / Math.Max(1, Count - _rows));
        }

        private void DragTo(int y)
        {
            var (track, thumb, _) = Bar();
            int span = Math.Max(1, track - thumb);
            _top = Math.Clamp((int)Math.Round((y - _dragOffset - 1) * (double)(Count - _rows) / span), 0, Count - _rows);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Scrolls || e.X < Width - BarW) return;
            var (_, thumb, ty) = Bar();
            _dragging = true;
            _dragOffset = e.Y >= ty && e.Y < ty + thumb ? e.Y - ty : thumb / 2;   // a press beside the thumb brings it under the cursor
            DragTo(e.Y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) { DragTo(e.Y); return; }
            if (!ClientRectangle.Contains(e.Location) || (Scrolls && e.X >= Width - BarW)) return;
            int i = RowAt(e.Y);
            if (i != Hot) { Hot = i; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging) { _dragging = false; return; }
            if (e.Button != MouseButtons.Left || !ClientRectangle.Contains(e.Location) || (Scrolls && e.X >= Width - BarW)) return;
            Hot = RowAt(e.Y);
            _field.CloseList(true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var bg = new SolidBrush(Theme.Surface)) g.FillRectangle(bg, ClientRectangle);
            int bar = Scrolls ? BarW : 0;
            for (int r = 0; r < _rows && _top + r < Count; r++)
            {
                int i = _top + r;
                var row = new Rectangle(1, 1 + r * RowH, Width - 2 - bar, RowH);
                bool hot = i == Hot;
                if (hot) { using var b = new SolidBrush(Theme.AccentFill); g.FillRectangle(b, row); }
                TextRenderer.DrawText(g, _field._items[i]?.ToString() ?? "", Font,
                    new Rectangle(row.X + 8, row.Y, row.Width - 10, row.Height), hot ? Color.White : Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            if (Scrolls)
            {
                var (_, thumb, ty) = Bar();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var tb = new SolidBrush(_dragging ? Theme.Muted : Theme.BorderStrong);
                using var tp = Theme.RoundRect(new RectangleF(Width - BarW + 2, ty + 2, BarW - 5, thumb - 4), 2);
                g.FillPath(tb, tp);
                g.SmoothingMode = SmoothingMode.Default;
            }
            using (var bp = new Pen(Theme.Border)) g.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
        }
    }

    // ---------------- accessibility ----------------

    protected override AccessibleObject CreateAccessibilityInstance() => new FieldAccessible(this);

    /// <summary>What a screen reader gets: a combo box whose value is the selected item's text.</summary>
    private sealed class FieldAccessible : ControlAccessibleObject
    {
        private readonly ThemedComboBox _field;
        public FieldAccessible(ThemedComboBox field) : base(field) => _field = field;
        public override AccessibleRole Role => AccessibleRole.ComboBox;
        public override string? Value { get => _field.Text; set { if (value != null) _field.Text = value; } }
        public override void DoDefaultAction() => _field.OpenList();
    }
}
