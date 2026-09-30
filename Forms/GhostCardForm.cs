using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// The GhostDeck decision card: a user-facing message with two text buttons, in the same
/// visual language as the game-session card (SessionReportForm) - dark card with the
/// cyan→violet rail, GhostDeck wordmark and a scan tag, rendered per-pixel with
/// UpdateLayeredWindow. It is a decision, so it takes focus, sits in the centre of the
/// screen, Enter = accent action, Esc / ✕ = later. Draggable by the body.
/// Carriers: the firmware guard (#212) and the Apex first-enable explainer; any future
/// user-facing question uses this rather than MessageBox/TaskDialog (RENDERING.md 11).
/// </summary>
public sealed class GhostCardForm : Form
{
    private readonly string _scanTag;
    private readonly string _heading;
    private readonly string _body;
    private readonly string _ackLabel;
    private readonly string _laterLabel;
    private readonly Action _onAck;
    private int _hotBtn = -1;                       // 0 = restore, 1 = later, 2 = ✕
    private readonly Rectangle[] _btn = new Rectangle[3];
    private Rectangle _cardRect;
    private bool _drag;
    private bool _dragMoved;
    private Point _dragOff;

    private static readonly Color Bg = Color.FromArgb(247, 0x10, 0x15, 0x1F);
    private static readonly Color White = Color.FromArgb(0xF3, 0xF7, 0xFF);
    private static readonly Color Muted = Color.FromArgb(0x98, 0xA0, 0xAE);
    private static readonly Color Ink = Color.FromArgb(0xC9, 0xD4, 0xE8);
    private static readonly Color Cyan = Color.FromArgb(0x3D, 0xE3, 0xFF);
    private static readonly Color Violet = Color.FromArgb(0x8D, 0x63, 0xFF);
    private static readonly Color Amber = Color.FromArgb(0xFF, 0xC1, 0x5D);

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED;
            return cp;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dst, ref SIZE size, IntPtr srcDc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);

    public GhostCardForm(string scanTag, string heading, string body,
                         string ackLabel, string laterLabel, Action onAck)
    {
        _scanTag = scanTag;
        _heading = heading;
        _body = body;
        _ackLabel = ackLabel;
        _laterLabel = laterLabel;
        _onAck = onAck;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        Icon = TrayIconFactory.AppIcon();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Render();
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        else if (e.KeyCode == Keys.Enter) { Ack(); e.Handled = true; }
    }

    private void Ack()
    {
        try { _onAck(); } catch { }
        Close();
    }

    // ---------------- mouse ----------------
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_hotBtn != -1) { _hotBtn = -1; Render(); } }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && _hotBtn == -1 && _cardRect.Contains(e.Location))
        {
            _drag = true;
            _dragMoved = false;
            _dragOff = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag)
        {
            int dx = e.X - _dragOff.X, dy = e.Y - _dragOff.Y;
            if (_dragMoved || Math.Abs(dx) > 4 || Math.Abs(dy) > 4)
            {
                _dragMoved = true;
                Location = new Point(Location.X + dx, Location.Y + dy);
            }
            return;
        }
        int hot = -1;
        for (int i = 0; i < _btn.Length; i++) if (_btn[i].Contains(e.Location)) hot = i;
        Cursor = hot >= 0 ? Cursors.Hand : _cardRect.Contains(e.Location) ? Cursors.SizeAll : Cursors.Default;
        if (hot != _hotBtn) { _hotBtn = hot; Render(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        bool wasDrag = _drag && _dragMoved;
        _drag = false;
        if (wasDrag) return;
        switch (_hotBtn)
        {
            case 0: Ack(); return;
            case 1: case 2: Close(); return;
        }
    }

    // ---------------- render ----------------
    private void Render()
    {
        if (!IsHandleCreated) return;
        using var bmp = Compose();
        if (Width != bmp.Width || Height != bmp.Height) Size = new Size(bmp.Width, bmp.Height);
        Push(bmp);
    }

    private Bitmap Compose()
    {
        float dpi; using (var mg = CreateGraphics()) dpi = mg.DpiY;
        float k = dpi / 96f;
        int Ce(float v) => (int)Math.Ceiling(v);

        int pad = Ce(14 * k);                        // transparent margin (shadow lives here)
        int W = Ce(430 * k);
        int railW = Ce(5 * k);
        int r = Ce(12 * k);
        int cx = railW + Ce(15 * k);
        int cw = W - cx - Ce(18 * k);

        using var wordF = new Font("Segoe UI", 11.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var scanF = new Font("Consolas", 10.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var titleF = new Font("Segoe UI", 14.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyF = new Font("Segoe UI", 11.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var btnF = new Font("Segoe UI", 11.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);

        // measure the body first - the card grows with the text (and with the language)
        int yHdr = Ce(14 * k), hHdr = Ce(24 * k);
        int yTitle = yHdr + hHdr + Ce(8 * k);
        int hTitle, hBody, hBtn = Ce(30 * k);
        using (var probe = Graphics.FromImage(new Bitmap(1, 1)))
        {
            probe.TextRenderingHint = TextRenderingHint.AntiAlias;
            hTitle = Ce(titleF.GetHeight(probe));
            hBody = Ce(probe.MeasureString(_body, bodyF, cw).Height);
        }
        int yBody = yTitle + hTitle + Ce(8 * k);
        int yBtns = yBody + hBody + Ce(14 * k);
        int H = yBtns + hBtn + Ce(14 * k);

        var bmp = new Bitmap(W + pad * 2, H + pad * 2, PixelFormat.Format32bppArgb);
        bmp.SetResolution(dpi, dpi);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.Clear(Color.Transparent);
        g.TranslateTransform(pad, pad);

        // card outline: square left corners, rounded right ones (the session-card shape, no tail)
        using var path = new GraphicsPath();
        path.StartFigure();
        path.AddLine(0, 0, W - r, 0);
        path.AddArc(W - 2 * r, 0, 2 * r, 2 * r, 270, 90);
        path.AddLine(W, r, W, H - r);
        path.AddArc(W - 2 * r, H - 2 * r, 2 * r, 2 * r, 0, 90);
        path.AddLine(W - r, H, 0, H);
        path.CloseFigure();

        // soft shadow: a few expanded low-alpha strokes (GDI+ has no blur)
        for (int i = 4; i >= 1; i--)
        {
            using var sp = new Pen(Color.FromArgb(7 * i, 0, 0, 0), i * 2.6f * k) { LineJoin = LineJoin.Round };
            var m = new Matrix(); m.Translate(0, 1.5f * k);
            using var sh = (GraphicsPath)path.Clone();
            sh.Transform(m);
            g.DrawPath(sp, sh);
        }

        using (var bb = new SolidBrush(Bg)) g.FillPath(bb, path);
        using (var bp = new Pen(Color.FromArgb(26, 243, 247, 255), Math.Max(1f, 1f * k)))
            g.DrawPath(bp, path);

        // rail: cyan→violet, flush with the square left edge
        using (var lg = new LinearGradientBrush(new RectangleF(0, 0, railW, H), Cyan, Violet, LinearGradientMode.Vertical))
            g.FillRectangle(lg, new RectangleF(0, 0, railW, H));

        // ---- header: ghost + wordmark, amber scan tag (a warning, not a session) ----
        int gs = Ce(21 * k);
        TrayIconFactory.DrawGhost(g, cx, yHdr + (hHdr - gs) / 2f, gs, Cyan, Color.FromArgb(0x0A, 0x0D, 0x14));
        using var whiteB = new SolidBrush(White);
        using var cyanB = new SolidBrush(Cyan);
        float wx = cx + gs + 6 * k, wy = yHdr + (hHdr - wordF.GetHeight(g)) / 2f;
        g.DrawString("Ghost", wordF, whiteB, wx, wy);
        float ghostW = g.MeasureString("Ghost", wordF, PointF.Empty, StringFormat.GenericTypographic).Width;
        g.DrawString("Deck", wordF, cyanB, wx + ghostW + 1 * k, wy);
        int xs = Ce(20 * k);                         // ✕ size, reserved right of the scan tag
        using (var scanB = new SolidBrush(Color.FromArgb(200, Amber)))
        {
            var sz = g.MeasureString(_scanTag, scanF);
            g.DrawString(_scanTag, scanF, scanB, cx + cw - sz.Width - xs - 8 * k, yHdr + (hHdr - sz.Height) / 2f);
        }

        // ---- heading + body ----
        g.DrawString(_heading, titleF, whiteB, cx - 2 * k, yTitle);
        using (var ib = new SolidBrush(Ink))
            g.DrawString(_body, bodyF, ib, new RectangleF(cx, yBody, cw, hBody + 4 * k));

        // ---- buttons: [accent action]  [later], right-aligned ----
        string ackTxt = _ackLabel, laterTxt = _laterLabel;
        int padX = Ce(14 * k), bgap = Ce(8 * k);
        int wAck = Ce(g.MeasureString(ackTxt, btnF).Width) + padX * 2;
        int wLater = Ce(g.MeasureString(laterTxt, btnF).Width) + padX * 2;
        int xLater = cx + cw - wLater, xAck = xLater - bgap - wAck;
        void Button(int i, int bx, int bw, string text, bool accent)
        {
            var rc = new Rectangle(bx, yBtns, bw, hBtn);
            _btn[i] = new Rectangle(rc.X + pad, rc.Y + pad, rc.Width, rc.Height);   // window coords
            bool hot = _hotBtn == i;
            using var fb = new SolidBrush(Color.FromArgb(hot ? 34 : 13, 255, 255, 255));
            using var rp = RoundPath(rc, Ce(8 * k));
            g.FillPath(fb, rp);
            Color edge = accent ? Color.FromArgb(hot ? 255 : 130, Cyan) : Color.FromArgb(hot ? 90 : 36, 243, 247, 255);
            using var op = new Pen(edge, 1f);
            g.DrawPath(op, rp);
            using var tb = new SolidBrush(accent ? Cyan : hot ? White : Ink);
            var sz = g.MeasureString(text, btnF);
            g.DrawString(text, btnF, tb, rc.X + (rc.Width - sz.Width) / 2f, rc.Y + (rc.Height - sz.Height) / 2f);
        }
        Button(0, xAck, wAck, ackTxt, accent: true);
        Button(1, xLater, wLater, laterTxt, accent: false);

        // small ✕ top-right (= Later)
        var xr = new Rectangle(W - xs - Ce(10 * k), yHdr + (hHdr - xs) / 2, xs, xs);
        _btn[2] = new Rectangle(xr.X + pad, xr.Y + pad, xr.Width, xr.Height);
        using (var xp = new Pen(_hotBtn == 2 ? White : Muted, Math.Max(1.2f, 1.5f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(xp, xr.X + xr.Width * .28f, xr.Y + xr.Height * .28f, xr.X + xr.Width * .72f, xr.Y + xr.Height * .72f);
            g.DrawLine(xp, xr.X + xr.Width * .72f, xr.Y + xr.Height * .28f, xr.X + xr.Width * .28f, xr.Y + xr.Height * .72f);
        }

        _cardRect = new Rectangle(pad, pad, W, H);
        return bmp;
    }

    private static GraphicsPath RoundPath(RectangleF r, int radius)
    {
        var p = new GraphicsPath();
        int d = Math.Max(2, radius * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private void Push(Bitmap bmp)
    {
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hbmp = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mem, hbmp);
        try
        {
            var size = new SIZE(bmp.Width, bmp.Height);
            var src = new POINT(0, 0);
            var dst = new POINT(Left, Top);
            var bf = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref bf, 2);
        }
        finally { SelectObject(mem, old); DeleteObject(hbmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); }
    }
}
