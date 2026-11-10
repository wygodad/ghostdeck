using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// The colours a colour setting offers, drawn the same way on a card and in the drop-down of a
/// colour button (<see cref="CardPopupPalette"/>): a "Default" pill with a dot of what default
/// means, then the palette as discs. The picked one wears a white ring. Item 0 is the pill,
/// items 1.. follow <see cref="DashPaint.PaletteIds"/>.
/// </summary>
internal static class CardPalette
{
    private static readonly Color White = Color.FromArgb(0xF3, 0xF7, 0xFF);
    private static readonly Color Ink = Color.FromArgb(0xC9, 0xD4, 0xE8);

    /// <summary>Where every item sits, the height they take together and how many discs make a row.</summary>
    public readonly record struct Plan(Rectangle[] Items, int Height, int Cols);

    public static Plan Layout(Graphics g, Font font, int x, int y, int width, float k, string defaultText)
    {
        int Ce(float v) => (int)Math.Ceiling(v);
        int n = DashPaint.PaletteIds.Length;
        int cell = Ce(30 * k), gap = Ce(8 * k), padX = Ce(11 * k), dot = Ce(14 * k);
        int textW = Ce(g.MeasureString(defaultText, font, PointF.Empty, StringFormat.GenericTypographic).Width);
        int pillW = Math.Min(width, padX + dot + Ce(7 * k) + textW + padX + Ce(2 * k));
        var items = new Rectangle[n + 1];
        items[0] = new Rectangle(x, y, pillW, cell);
        // side by side when it all fits; else the pill on a line of its own and the discs under
        // it, in one row when they fit at a size still easy to hit, in two when they do not
        bool oneLine = pillW + gap + n * cell <= width;
        int cols = oneLine || n * Ce(26 * k) <= width ? n : (n + 1) / 2;
        int cw = oneLine ? cell : Math.Min(cell, width / cols);
        int left = oneLine ? x + pillW + gap : x, top = oneLine ? y : y + cell + gap;
        for (int i = 0; i < n; i++)
            items[i + 1] = new Rectangle(left + i % cols * cw, top + i / cols * cw, cw, cw);
        int rows = (n + cols - 1) / cols;
        return new Plan(items, Math.Max(cell, top + rows * cw - y), cols);
    }

    /// <summary><paramref name="current"/> is "auto" or a palette id; <paramref name="hot"/> the item under the cursor, -1 = none.</summary>
    public static void Paint(Graphics g, Font font, Plan p, float k, Color auto, string defaultText, string current, int hot)
    {
        using var fmt = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        for (int i = 0; i < p.Items.Length; i++)
        {
            var r = p.Items[i];
            bool sel = DashPaint.PaletteChoices[i] == current, isHot = i == hot;
            if (i > 0)
            {
                float inset = Math.Max(2.5f, r.Width * 0.12f);
                Disc(g, new RectangleF(r.X + inset, r.Y + inset, r.Width - inset * 2, r.Height - inset * 2), DashPaint.PaletteColor(DashPaint.PaletteIds[i - 1]));
                if (!sel && !isHot) continue;
                using var ring = new Pen(sel ? White : Color.FromArgb(110, White), Math.Max(1.5f, 2f * k));
                float half = ring.Width / 2f;
                g.DrawEllipse(ring, r.X + half, r.Y + half, r.Width - ring.Width, r.Height - ring.Width);
                continue;
            }
            // the pill: what "default" is, in words and as a dot of its colour
            var pr = new RectangleF(r.X + k, r.Y + 2 * k, r.Width - 2 * k, r.Height - 4 * k);
            using var path = Round(pr, pr.Height / 2f);
            using (var fill = new SolidBrush(Color.FromArgb(sel ? 24 : isHot ? 20 : 10, 255, 255, 255))) g.FillPath(fill, path);
            using (var edge = new Pen(sel ? White : Color.FromArgb(isHot ? 110 : 40, White), sel ? Math.Max(1.5f, 2f * k) : 1f)) g.DrawPath(edge, path);
            float dot = (float)Math.Ceiling(14 * k), dx = pr.X + 10 * k;
            Disc(g, new RectangleF(dx, pr.Y + (pr.Height - dot) / 2f, dot, dot), auto);
            using var tb = new SolidBrush(sel || isHot ? White : Ink);
            float tx = dx + dot + 7 * k;
            g.DrawString(defaultText, font, tb, new RectangleF(tx, pr.Y, Math.Max(1, pr.Right - 8 * k - tx), pr.Height), fmt);
        }
    }

    /// <summary>A colour as a disc; a colour too dark to see on a card gets a thin light edge.</summary>
    public static void Disc(Graphics g, RectangleF r, Color c)
    {
        using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
        if (c.GetBrightness() >= 0.3f) return;
        using var p = new Pen(Color.FromArgb(c.A * 120 / 255, White), 1f);
        g.DrawEllipse(p, r);
    }

    private static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Max(2f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>
/// The drop-down of a colour button on a GhostDeck card: the palette (<see cref="CardPalette"/>)
/// in an owned, borderless window, with the manners of <see cref="CardPopupList"/>: it closes on
/// a pick, on Esc and when it loses activation; the arrows and Enter work from the keyboard.
/// </summary>
internal sealed class CardPopupPalette : Form
{
    private static readonly Color Back = Color.FromArgb(0x17, 0x1D, 0x29);
    private static readonly Color Edge = Color.FromArgb(0x33, 0x3C, 0x4E);

    private static object? _lastKey;
    private static DateTime _closedAt = DateTime.MinValue;

    private readonly Color _auto;
    private readonly string _autoText, _current;
    private readonly float _k;
    private readonly Action<string> _onPick;
    private readonly object _key;
    private readonly Font _font;
    private readonly CardPalette.Plan _plan;
    private readonly Size _size;
    private int _hot;
    private bool _closing;

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private CardPopupPalette(Color auto, string autoText, string current, float k, object key, Action<string> onPick)
    {
        _auto = auto; _autoText = autoText; _current = current; _k = k; _key = key; _onPick = onPick;
        _hot = Math.Max(0, Array.IndexOf(DashPaint.PaletteChoices, current));
        _font = NewFont(k);
        (_plan, _size) = Measure(_font, autoText, k);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        BackColor = Back;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    private static Font NewFont(float k) => Dpi.Font("Segoe UI Semibold", 13f * k, FontStyle.Regular, GraphicsUnit.Pixel);

    /// <summary>Two rows of discs under the pill, with a margin around.</summary>
    private static (CardPalette.Plan plan, Size size) Measure(Font font, string autoText, float k)
    {
        int pad = (int)Math.Ceiling(9 * k), cell = (int)Math.Ceiling(30 * k);
        int inner = (DashPaint.PaletteIds.Length + 1) / 2 * cell;
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        var plan = CardPalette.Layout(g, font, pad, pad, inner, k, autoText);
        return (plan, new Size(inner + pad * 2, plan.Height + pad * 2));
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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2;   // DWMWCP_ROUND; Windows 10 has no such attribute and keeps square corners
        try { DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); } catch { }
    }

    /// <summary>
    /// Opens the palette under <paramref name="anchor"/> (screen coordinates), its right edge on
    /// the button's, or does nothing if the same button closed its palette a moment ago - that
    /// click was the one that dismissed it. <paramref name="onPick"/> gets "auto" or a palette id.
    /// </summary>
    public static void Open(Form owner, Rectangle anchor, Color auto, string autoText, string current, float k, object key, Action<string> onPick)
    {
        if (ReferenceEquals(_lastKey, key) && (DateTime.UtcNow - _closedAt).TotalMilliseconds < 250) { _lastKey = null; return; }
        var p = new CardPopupPalette(auto, autoText, current, k, key, onPick);
        var wa = Screen.FromRectangle(anchor).WorkingArea;
        var size = p._size;
        int gap = (int)Math.Ceiling(4 * k), w = size.Width, h = size.Height;
        int x = Math.Max(wa.Left + 8, Math.Min(anchor.Right - w, wa.Right - 8 - w));
        int y = anchor.Bottom + gap;
        if (y + h > wa.Bottom - 8) y = Math.Max(wa.Top + 8, anchor.Top - gap - h);
        p.Bounds = new Rectangle(x, y, w, h);
        p.Show(owner);
        p.Activate();
    }

    /// <summary>The palette as a picture, for the preview tool.</summary>
    internal static Bitmap Picture(Color auto, string autoText, string current, float k, int hot)
    {
        using var font = NewFont(k);
        var (plan, size) = Measure(font, autoText, k);
        var bmp = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(bmp);
        Draw(g, size, font, plan, k, auto, autoText, current, hot);
        return bmp;
    }

    private static void Draw(Graphics g, Size size, Font font, CardPalette.Plan plan, float k, Color auto, string autoText, string current, int hot)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.Clear(Back);
        using (var ep = new Pen(Edge)) g.DrawRectangle(ep, 0, 0, size.Width - 1, size.Height - 1);
        CardPalette.Paint(g, font, plan, k, auto, autoText, current, hot);
    }

    protected override void OnPaint(PaintEventArgs e) => Draw(e.Graphics, ClientSize, _font, _plan, _k, _auto, _autoText, _current, _hot);

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
        _font.Dispose();
    }

    private void Pick(int i)
    {
        if (_closing || i < 0 || i >= _plan.Items.Length) return;
        CloseOnce();
        _onPick(DashPaint.PaletteChoices[i]);
    }

    private int ItemAt(Point p)
    {
        for (int i = 0; i < _plan.Items.Length; i++) if (_plan.Items[i].Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = ItemAt(e.Location);
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        if (i >= 0 && i != _hot) { _hot = i; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) Pick(ItemAt(e.Location));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        int last = _plan.Items.Length - 1, cols = _plan.Cols, hot = _hot;
        switch (keyData)
        {
            case Keys.Escape: CloseOnce(); return true;
            case Keys.Enter: case Keys.Space: Pick(_hot); return true;
            case Keys.Left: hot = Math.Max(0, _hot - 1); break;
            case Keys.Right: hot = Math.Min(last, _hot + 1); break;
            // the pill is the row above the discs
            case Keys.Up: hot = _hot - cols >= 1 ? _hot - cols : 0; break;
            case Keys.Down: hot = _hot == 0 ? 1 : _hot + cols <= last ? _hot + cols : _hot; break;
            default: return base.ProcessCmdKey(ref msg, keyData);
        }
        if (hot != _hot) { _hot = hot; Invalidate(); }
        return true;
    }
}
