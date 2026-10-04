using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// A real text box riding on a GhostDeck card. The card is a layered window and cannot host
/// child controls, so the editable text lives in this small owned, borderless window placed
/// exactly over the field the card paints (same opaque background, so no seam shows). It
/// brings what a painted field never would: selection, clipboard, IME and the emoji panel.
/// The card keeps it aligned from <c>OnRendered</c> / <c>OnMove</c>.
/// </summary>
internal sealed class CardTextHost : Form
{
    public TextBox Box { get; } = new();

    /// <summary>Enter, Escape or Tab was pressed in the box - the card decides what they mean.</summary>
    public event Action<Keys>? CommandKey;

    public CardTextHost(Color back, Color fore, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = back;
        Box.BorderStyle = BorderStyle.None;
        Box.BackColor = back;
        Box.ForeColor = fore;
        Box.TextAlign = align;
        Controls.Add(Box);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & ~Keys.Shift;
        if (key is Keys.Enter or Keys.Escape or Keys.Tab) { CommandKey?.Invoke(key); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Places the host over a screen rectangle; the box is centred vertically in it.</summary>
    public void Place(Rectangle screen, Font font)
    {
        if (!ReferenceEquals(Box.Font, font)) Box.Font = font;
        if (Bounds != screen) Bounds = screen;
        int h = Box.PreferredHeight;
        var want = new Rectangle(0, Math.Max(0, (screen.Height - h) / 2), screen.Width, h);
        if (Box.Bounds != want) Box.Bounds = want;
    }
}

/// <summary>
/// The drop-down of a painted select field on a GhostDeck card: an owned, borderless list in
/// the card palette. Closes on a pick, on Esc and when it loses activation; the wheel scrolls
/// a long list, ↑/↓ + Enter work from the keyboard. Items may carry an icon painter (the
/// profile list shows the scenario icons).
/// </summary>
internal sealed class CardPopupList : Form
{
    /// <summary><paramref name="Icon"/> gets the box and whether it sits on the selection fill (then it must be white).</summary>
    public sealed record Item(string Text, Action<Graphics, RectangleF, bool>? Icon = null);

    private const int MaxVisible = 9;
    private static readonly Color Back = Color.FromArgb(0x17, 0x1D, 0x29);
    private static readonly Color Edge = Color.FromArgb(0x33, 0x3C, 0x4E);
    private static readonly Color White = Color.FromArgb(0xF3, 0xF7, 0xFF);
    private static readonly Color Ink = Color.FromArgb(0xC9, 0xD4, 0xE8);
    private static readonly Color Fill = Color.FromArgb(0x3C, 0x7D, 0xFF);

    private static object? _lastKey;
    private static DateTime _closedAt = DateTime.MinValue;

    private readonly IReadOnlyList<Item> _items;
    private readonly int _selected;
    private readonly float _k;
    private readonly Action<int> _onPick;
    private readonly object _key;
    private readonly int _itemH, _pad;
    private int _top, _hot;
    private bool _closing;

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private CardPopupList(IReadOnlyList<Item> items, int selected, float k, object key, Action<int> onPick)
    {
        _items = items; _selected = selected; _k = k; _key = key; _onPick = onPick;
        _hot = selected;
        _itemH = (int)Math.Ceiling(32 * k);
        _pad = (int)Math.Ceiling(4 * k);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        BackColor = Back;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    private int RowsShown => Math.Min(_items.Count, MaxVisible);

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2;   // DWMWCP_ROUND; Windows 10 has no such attribute and keeps square corners
        try { DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); } catch { }
    }

    /// <summary>
    /// Opens the list under <paramref name="anchor"/> (screen coordinates), or does nothing if the
    /// same field closed its list a moment ago - that click was the one that dismissed it.
    /// </summary>
    public static void Open(Form owner, Rectangle anchor, IReadOnlyList<Item> items, int selected, float k, object key, Action<int> onPick)
    {
        if (ReferenceEquals(_lastKey, key) && (DateTime.UtcNow - _closedAt).TotalMilliseconds < 250) { _lastKey = null; return; }
        var p = new CardPopupList(items, selected, k, key, onPick);
        int h = p.RowsShown * p._itemH + p._pad * 2;
        var wa = Screen.FromRectangle(anchor).WorkingArea;
        int gap = (int)Math.Ceiling(4 * k);
        int y = anchor.Bottom + gap;
        if (y + h > wa.Bottom - 8) y = Math.Max(wa.Top + 8, anchor.Top - gap - h);
        p.Bounds = new Rectangle(anchor.X, y, anchor.Width, h);
        p._top = Math.Clamp(selected - p.RowsShown / 2, 0, Math.Max(0, items.Count - p.RowsShown));
        p.Show(owner);
        p.Activate();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        CloseOnce();
    }

    private void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _lastKey = _key;
        _closedAt = DateTime.UtcNow;
    }

    private void Pick(int i)
    {
        if (_closing || i < 0 || i >= _items.Count) return;
        CloseOnce();
        _onPick(i);
    }

    private int ItemAt(Point p)
    {
        if (p.X < 0 || p.X >= Width || p.Y < _pad) return -1;
        int i = _top + (p.Y - _pad) / _itemH;
        return i < _top + RowsShown && i < _items.Count ? i : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = ItemAt(e.Location);
        if (i >= 0 && i != _hot) { _hot = i; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) Pick(ItemAt(e.Location));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int max = Math.Max(0, _items.Count - RowsShown);
        int top = Math.Clamp(_top - Math.Sign(e.Delta) * 2, 0, max);
        if (top == _top) return;
        _top = top;
        int i = ItemAt(e.Location);
        if (i >= 0) _hot = i;
        Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape: CloseOnce(); return true;
            case Keys.Enter: case Keys.Space: Pick(_hot); return true;
            case Keys.Up: case Keys.Down:
                _hot = Math.Clamp(_hot + (keyData == Keys.Down ? 1 : -1), 0, _items.Count - 1);
                if (_hot < _top) _top = _hot;
                if (_hot >= _top + RowsShown) _top = _hot - RowsShown + 1;
                Invalidate();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.Clear(Back);
        using (var ep = new Pen(Edge)) g.DrawRectangle(ep, 0, 0, Width - 1, Height - 1);

        using var font = new Font("Segoe UI", 13.5f * _k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var bold = new Font("Segoe UI", 13.5f * _k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var fmt = new StringFormat(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var whiteB = new SolidBrush(White);
        using var inkB = new SolidBrush(Ink);
        bool scrolls = _items.Count > RowsShown;
        int barW = scrolls ? (int)Math.Ceiling(8 * _k) : 0;
        int icon = (int)Math.Ceiling(18 * _k), tx = (int)Math.Ceiling(12 * _k);

        for (int n = 0; n < RowsShown; n++)
        {
            int i = _top + n;
            if (i >= _items.Count) break;
            var rc = new Rectangle(_pad, _pad + n * _itemH, Width - _pad * 2 - barW, _itemH);
            bool sel = i == _selected, hot = i == _hot;
            if (sel || hot)
            {
                var fr = new RectangleF(rc.X, rc.Y + 1, rc.Width, rc.Height - 2);
                using var rp = RoundRect(fr, 6 * _k);
                using var fb = new SolidBrush(sel ? Fill : Color.FromArgb(26, 255, 255, 255));
                g.FillPath(fb, rp);
            }
            int x = rc.X + tx;
            if (_items[i].Icon is { } paint)
            {
                paint(g, new RectangleF(x, rc.Y + (rc.Height - icon) / 2f, icon, icon), sel);
                x += icon + (int)Math.Ceiling(10 * _k);
            }
            g.DrawString(_items[i].Text, sel ? bold : font, sel || hot ? whiteB : inkB,
                new RectangleF(x, rc.Y, rc.Right - x - tx / 2, rc.Height), fmt);
        }

        if (scrolls)
        {
            float trackH = Height - _pad * 2, thumbH = Math.Max(18 * _k, trackH * RowsShown / _items.Count);
            float thumbY = _pad + (trackH - thumbH) * _top / Math.Max(1, _items.Count - RowsShown);
            using var tb = new SolidBrush(Color.FromArgb(70, 243, 247, 255));
            using var tp = RoundRect(new RectangleF(Width - _pad - barW + 2 * _k, thumbY, barW - 4 * _k, thumbH), 2 * _k);
            g.FillPath(tb, tp);
        }
    }

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Max(2f, radius * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
