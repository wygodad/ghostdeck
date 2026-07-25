using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace GhostDeck;

/// <summary>
/// Borderless game-session summary popup (replaces the plain tray balloon). Design picked by the
/// user from the W-series mockups: W4 speech-bubble base (thin border + tail pointing at the tray)
/// crossed with W2's vertical cyan→violet rail on a flat left edge — square left corners, softly
/// rounded right ones, GhostDeck wordmark and a "//SESSION-END" scan tag. Rendered per-pixel with
/// UpdateLayeredWindow (same technique as OverlayForm), so the irregular shape has true alpha and
/// no window chrome. Never steals focus. Actions: save the card as PNG, export the session data
/// (JSON/CSV), close; clicking the body opens Status → Gaming. Auto-hides after ~20 s (paused
/// while hovered, thin countdown bar along the bottom).
/// </summary>
public sealed class SessionReportForm : Form
{
    private readonly GameSession _s;
    private readonly Action _openGaming;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };
    private readonly int _autoCloseMs;              // from Settings; 0 = stays until closed
    private int _remainingMs;
    private bool _hover;
    private bool _pinned;                           // any interaction pins the popup: only ✕ closes it
    private int _hotBtn = -1;                       // 0 = open Gaming, 1 = png, 2 = export, 3 = close
    private readonly Rectangle[] _btn = new Rectangle[4];
    private Rectangle _cardRect;                    // opaque card area (grab anywhere to drag)
    private bool _drag;
    private bool _dragMoved;
    private Point _dragOff;

    private static readonly Color Bg = Color.FromArgb(247, 0x10, 0x15, 0x1F);
    private static readonly Color White = Color.FromArgb(0xF3, 0xF7, 0xFF);
    private static readonly Color Muted = Color.FromArgb(0x98, 0xA0, 0xAE);
    private static readonly Color Cyan = Color.FromArgb(0x3D, 0xE3, 0xFF);
    private static readonly Color Violet = Color.FromArgb(0x8D, 0x63, 0xFF);
    private static readonly Color Amber = Color.FromArgb(0xFF, 0xC1, 0x5D);
    private static readonly Color Red = Color.FromArgb(0xFF, 0x2F, 0x7D);
    private static readonly Color Blue = Color.FromArgb(0x3C, 0x7D, 0xFF);

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
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

    public SessionReportForm(GameSession s, Action openGaming, int autoCloseSeconds)
    {
        _s = s;
        _openGaming = openGaming;
        _autoCloseMs = Math.Max(0, autoCloseSeconds) * 1000;
        _remainingMs = _autoCloseMs;
        _pinned = _autoCloseMs == 0;                // "until closed" mode
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        _timer.Tick += (_, _) =>
        {
            if (!_pinned)
            {
                if (!_hover) _remainingMs -= _timer.Interval;
                if (_remainingMs <= 0) { Close(); return; }
            }
            Render();
        };
    }

    // Any deliberate interaction pins the popup - from then on only the ✕ closes it.
    private void Pin() { if (!_pinned) { _pinned = true; Render(); } }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Render();
        Place();
        _timer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e) { _timer.Stop(); base.OnFormClosed(e); }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    // bottom-right of the working area, tail pointing down-right toward the tray
    private void Place()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        Location = new Point(wa.Right - Width - 12, wa.Bottom - Height - 8);
    }

    // ---------------- mouse ----------------
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Render(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; if (_hotBtn != -1) { _hotBtn = -1; } Render(); }

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
                Pin();   // a moved popup stays until closed
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
        if (wasDrag) return;   // a move is not a click
        switch (_hotBtn)
        {
            case 0: Pin(); _openGaming(); return;   // deep-link keeps the popup open
            case 1: SavePng(); return;
            case 2: ExportData(); return;
            case 3: Close(); return;
        }
        // clicking the body does nothing - the body is the drag handle
    }

    // ---------------- actions ----------------
    private void SavePng()
    {
        Pin();
        _timer.Stop();
        try
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "PNG (*.png)|*.png",
                FileName = $"ghostdeck-session-{San(_s.Process)}-{_s.End:yyyyMMdd-HHmm}.png",
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                using var bmp = Compose(screenshot: true);
                bmp.Save(dlg.FileName, ImageFormat.Png);
            }
        }
        catch { }
        _timer.Start();
    }

    private void ExportData()
    {
        Pin();
        _timer.Stop();
        try { GameSessions.ExportWithDialog(_s, this); }
        catch { }
        _timer.Start();
    }

    private static string San(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    // ---------------- render ----------------
    private void Render()
    {
        if (!IsHandleCreated) return;
        using var bmp = Compose(screenshot: false);
        if (Width != bmp.Width || Height != bmp.Height) Size = new Size(bmp.Width, bmp.Height);
        Push(bmp);
    }

    /// <summary>Compose the card. Screenshot mode drops the buttons, countdown and tail —
    /// the saved PNG is just the clean card.</summary>
    private Bitmap Compose(bool screenshot)
    {
        float dpi; using (var mg = CreateGraphics()) dpi = mg.DpiY;
        float k = dpi / 96f;
        int Ce(float v) => (int)Math.Ceiling(v);

        int pad = Ce(14 * k);                        // transparent margin (shadow lives here)
        int W = Ce(430 * k);
        int railW = Ce(5 * k);
        int r = Ce(12 * k);                          // right-corner radius (left stays square)
        int cx = railW + Ce(15 * k);                 // content left
        int cw = W - cx - Ce(18 * k);                // content width

        // vertical layout
        int yHdr = Ce(14 * k), hHdr = Ce(24 * k);
        int yGame = yHdr + hHdr + Ce(6 * k), hGame = Ce(24 * k);
        int yBox = yGame + hGame + Ce(10 * k), hBox = Ce(46 * k);
        int ySpark = yBox + hBox + Ce(10 * k), hSpark = Ce(44 * k);
        int yFoot = ySpark + hSpark + Ce(10 * k), hFoot = Ce(30 * k);
        int H = yFoot + hFoot + Ce(12 * k);
        int tailW = Ce(24 * k), tailH = Ce(15 * k), tailX = W - Ce(56 * k);

        var bmp = new Bitmap(W + pad * 2, H + tailH + pad * 2, PixelFormat.Format32bppArgb);
        bmp.SetResolution(dpi, dpi);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.Clear(Color.Transparent);
        g.TranslateTransform(pad, pad);

        // card outline: square left corners, rounded right ones, W4 tail on the bottom edge
        using var path = new GraphicsPath();
        path.StartFigure();
        path.AddLine(0, 0, W - r, 0);
        path.AddArc(W - 2 * r, 0, 2 * r, 2 * r, 270, 90);
        path.AddLine(W, r, W, H - r);
        path.AddArc(W - 2 * r, H - 2 * r, 2 * r, 2 * r, 0, 90);
        if (!screenshot)
        {
            path.AddLine(W - r, H, tailX + tailW, H);
            path.AddLine(tailX + tailW, H, tailX + tailW, H + tailH);   // tail: vertical right side,
            path.AddLine(tailX + tailW, H + tailH, tailX, H);           // tip aims at the tray corner
        }
        path.AddLine(tailX, H, 0, H);
        path.CloseFigure();

        // soft shadow: a few expanded low-alpha strokes (GDI+ has no blur)
        if (!screenshot)
            for (int i = 4; i >= 1; i--)
            {
                using var sp = new Pen(Color.FromArgb(7 * i, 0, 0, 0), i * 2.6f * k) { LineJoin = LineJoin.Round };
                var m = new Matrix(); m.Translate(0, 1.5f * k);
                using var sh = (GraphicsPath)path.Clone();
                sh.Transform(m);
                g.DrawPath(sp, sh);
            }

        using (var bb = new SolidBrush(Bg)) g.FillPath(bb, path);

        // thin border everywhere but the left edge (the rail covers that side)
        using (var bp = new Pen(Color.FromArgb(26, 243, 247, 255), Math.Max(1f, 1f * k)))
            g.DrawPath(bp, path);

        // W2 rail: cyan→violet, flush with the square left edge
        var railRect = new RectangleF(0, 0, railW, H);
        using (var lg = new LinearGradientBrush(railRect, Cyan, Violet, LinearGradientMode.Vertical))
            g.FillRectangle(lg, railRect);

        // ---- header: ghost + wordmark, scan tag on the right ----
        int gs = Ce(21 * k);
        TrayIconFactory.DrawGhost(g, cx, yHdr + (hHdr - gs) / 2f, gs, Cyan, Color.FromArgb(0x0A, 0x0D, 0x14));
        using var wordF = new Font("Segoe UI", 11.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var whiteB = new SolidBrush(White);
        using var cyanB = new SolidBrush(Cyan);
        using var mutedB = new SolidBrush(Muted);
        float wx = cx + gs + 6 * k, wy = yHdr + (hHdr - wordF.GetHeight(g)) / 2f;
        g.DrawString("Ghost", wordF, whiteB, wx, wy);
        float ghostW = g.MeasureString("Ghost", wordF, PointF.Empty, StringFormat.GenericTypographic).Width;
        g.DrawString("Deck", wordF, cyanB, wx + ghostW + 1 * k, wy);
        using (var scanF = new Font("Consolas", 10.5f * k, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var scanB = new SolidBrush(Color.FromArgb(170, Cyan)))
        {
            var sz = g.MeasureString("//SESSION-END", scanF);
            g.DrawString("//SESSION-END", scanF, scanB, cx + cw - sz.Width, yHdr + (hHdr - sz.Height) / 2f);
        }

        // ---- game name + duration (the time sits right under //SESSION-END, close to it) ----
        using var gameF = new Font("Segoe UI", 14.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var monoF = new Font("Consolas", 12f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString(_s.Process, gameF, whiteB, cx - 2 * k, yGame);
        string when = $"{FmtDur(_s.End - _s.Start)} · {_s.End:HH:mm}";
        var whenSz = g.MeasureString(when, monoF);
        g.DrawString(when, monoF, mutedB, cx + cw - whenSz.Width, yHdr + hHdr + 1 * k);

        // ---- stat boxes ----
        using var lblF = new Font("Consolas", 10.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var valF = new Font("Segoe UI", 14f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var unitF = new Font("Segoe UI", 9.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        int gap = Ce(8 * k), bw = (cw - gap * 3) / 4;
        void Box(int i, string label, string value, Color valCol, string unit = "")
        {
            var rc = new RectangleF(cx + i * (bw + gap), yBox, bw, hBox);
            using var fb = new SolidBrush(Color.FromArgb(12, 255, 255, 255));
            using var rp = RoundPath(rc, Ce(7 * k));
            g.FillPath(fb, rp);
            using var ob = new Pen(Color.FromArgb(18, 243, 247, 255), 1f);
            g.DrawPath(ob, rp);
            using var lb = new SolidBrush(Muted);
            g.DrawString(label, lblF, lb, rc.X + 8 * k, rc.Y + 5 * k);
            using var vb = new SolidBrush(valCol);
            float vy = rc.Y + hBox - valF.GetHeight(g) - 4 * k;
            g.DrawString(value, valF, vb, rc.X + 7 * k, vy);
            if (unit.Length > 0)
            {
                // small grey unit right after the value (e.g. "3.0k RPM")
                float vw = g.MeasureString(value, valF, PointF.Empty, StringFormat.GenericTypographic).Width;
                g.DrawString(unit, unitF, lb, rc.X + 7 * k + vw + 5 * k, vy + (valF.GetHeight(g) - unitF.GetHeight(g)) - 2 * k);
            }
        }
        Box(0, "AVG FPS", _s.AvgFps.ToString(), Cyan);
        Box(1, "1% LOW", _s.P1LowFps > 0 ? _s.P1LowFps.ToString() : "—", White);
        Box(2, "CPU MAX", _s.MaxCpuTemp > 0 ? $"{_s.MaxCpuTemp}°" : "—", _s.MaxCpuTemp > 0 ? Amber : Muted);
        Box(3, "FAN AVG", _s.AvgCpuRpm > 0 ? FmtRpm(Math.Max(_s.AvgCpuRpm, _s.AvgGpuRpm)) : "—", White,
            _s.AvgCpuRpm > 0 ? "RPM" : "");

        // ---- frametime sparkline ----
        var spRect = new RectangleF(cx, ySpark, cw, hSpark);
        using (var sb2 = new SolidBrush(Color.FromArgb(64, 0, 0, 0)))
        using (var sp2 = RoundPath(spRect, Ce(6 * k)))
            g.FillPath(sb2, sp2);
        DrawSpark(g, spRect, k);

        // ---- footer: profile + counters, action buttons on the right ----
        float fy = yFoot + (hFoot - 12 * k) / 2f;
        using (var db = new SolidBrush(Blue)) g.FillEllipse(db, cx, fy + 1 * k, 9 * k, 9 * k);
        using var footF = new Font("Segoe UI", 11.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        string foot = (_s.Profile.Length > 0 ? _s.Profile + "  ·  " : "") +
                      $"{Lang.T("gm_stut")}: {_s.Stutters:N0}  ·  {Lang.T("gm_frames")}: {_s.Frames:N0}";
        g.DrawString(foot, footF, mutedB, cx + 14 * k, yFoot + (hFoot - footF.GetHeight(g)) / 2f);

        if (!screenshot)
        {
            // [open Gaming tab] [save PNG] [export data] [close]
            int bs = Ce(29 * k), bgap = Ce(7 * k);
            int bx = cx + cw - bs * 4 - bgap * 3, by = yFoot + (hFoot - bs) / 2;
            for (int i = 0; i < 4; i++)
            {
                var rc = new Rectangle(bx + i * (bs + bgap), by, bs, bs);
                _btn[i] = new Rectangle(rc.X + pad, rc.Y + pad, rc.Width, rc.Height);   // window coords
                bool hot = _hotBtn == i;
                using var fb = new SolidBrush(Color.FromArgb(hot ? 34 : 13, 255, 255, 255));
                using var rp = RoundPath(rc, Ce(8 * k));
                g.FillPath(fb, rp);
                Color edge = i == 1 ? Color.FromArgb(hot ? 255 : 130, Cyan) : Color.FromArgb(hot ? 90 : 36, 243, 247, 255);
                using var op = new Pen(edge, 1f);
                g.DrawPath(op, rp);
                Color ink = i == 1 ? Cyan : hot ? White : Color.FromArgb(0xC9, 0xD4, 0xE8);
                DrawBtnIcon(g, i, rc, ink, k);
            }

            // countdown bar along the TOP edge (at the bottom it used to cross the tail);
            // hidden once the popup is pinned (interaction or "until closed" mode)
            if (!_pinned && _autoCloseMs > 0)
            {
                float frac = Math.Clamp(_remainingMs / (float)_autoCloseMs, 0f, 1f);
                using var cb = new SolidBrush(Color.FromArgb(110, Cyan));
                g.FillRectangle(cb, railW, 1f * k, (W - railW - r) * frac, 2.5f * k);
            }
        }
        else
        {
            for (int i = 0; i < _btn.Length; i++) _btn[i] = Rectangle.Empty;
        }

        _cardRect = new Rectangle(pad, pad, W, H);
        return bmp;
    }

    private void DrawSpark(Graphics g, RectangleF rc, float k)
    {
        var d = _s.Spark;
        if (d == null || d.Length < 4) return;
        var peak = _s.SparkPeak;
        float mx = 0; foreach (var v in d) mx = Math.Max(mx, v);
        if (peak != null) foreach (var v in peak) mx = Math.Max(mx, v);
        mx = Math.Max(10f, mx * 1.15f);
        float px = 6 * k, py = 5 * k;
        float X(int i) => rc.X + px + (rc.Width - px * 2) * i / (d.Length - 1);
        float Y(float v) => rc.Bottom - py - (rc.Height - py * 2) * Math.Clamp(v, 0, mx) / mx;

        var pts = new PointF[d.Length];
        for (int i = 0; i < d.Length; i++) pts[i] = new PointF(X(i), Y(d[i]));
        using (var area = new GraphicsPath())
        {
            area.AddLines(pts);
            area.AddLine(pts[^1].X, rc.Bottom - py, pts[0].X, rc.Bottom - py);
            area.CloseFigure();
            using var lg = new LinearGradientBrush(rc, Color.FromArgb(60, Cyan), Color.FromArgb(4, Cyan), LinearGradientMode.Vertical);
            g.FillPath(lg, area);
        }
        using (var pen = new Pen(Cyan, Math.Max(1f, 1.3f * k)) { LineJoin = LineJoin.Round })
            g.DrawLines(pen, pts);

        if (peak != null && peak.Length == d.Length)
        {
            // stutter dots where the bucket peak clearly leaves the average line
            float median = Median(d);
            float at = Math.Max(25f, 2f * median);
            using var rb = new SolidBrush(Red);
            for (int i = 0; i < peak.Length; i++)
                if (peak[i] > at)
                    g.FillEllipse(rb, X(i) - 2.2f * k, Y(peak[i]) - 2.2f * k, 4.4f * k, 4.4f * k);
        }
    }

    private static float Median(float[] a)
    {
        var c = (float[])a.Clone();
        Array.Sort(c);
        return c[c.Length / 2];
    }

    private static void DrawBtnIcon(Graphics g, int kind, Rectangle rc, Color ink, float k)
    {
        using var pen = new Pen(ink, Math.Max(1.2f, 1.5f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float x = rc.X, y = rc.Y, w = rc.Width, h = rc.Height;
        switch (kind)
        {
            case 0:   // mini chart → open Status → Gaming
                g.DrawLine(pen, x + w * .24f, y + h * .24f, x + w * .24f, y + h * .74f);
                g.DrawLine(pen, x + w * .24f, y + h * .74f, x + w * .78f, y + h * .74f);
                g.DrawLine(pen, x + w * .32f, y + h * .58f, x + w * .46f, y + h * .42f);
                g.DrawLine(pen, x + w * .46f, y + h * .42f, x + w * .58f, y + h * .52f);
                g.DrawLine(pen, x + w * .58f, y + h * .52f, x + w * .74f, y + h * .30f);
                break;
            case 1:   // camera → save PNG
                g.DrawPath(pen, RoundPath(new RectangleF(x + w * .20f, y + h * .32f, w * .60f, h * .42f), (int)(2 * k)));
                g.DrawLine(pen, x + w * .38f, y + h * .32f, x + w * .44f, y + h * .22f);
                g.DrawLine(pen, x + w * .44f, y + h * .22f, x + w * .58f, y + h * .22f);
                g.DrawLine(pen, x + w * .58f, y + h * .22f, x + w * .64f, y + h * .32f);
                g.DrawEllipse(pen, x + w * .40f, y + h * .40f, w * .20f, h * .26f);
                break;
            case 2:   // arrow down → export data
                g.DrawLine(pen, x + w * .5f, y + h * .22f, x + w * .5f, y + h * .60f);
                g.DrawLine(pen, x + w * .34f, y + h * .46f, x + w * .5f, y + h * .60f);
                g.DrawLine(pen, x + w * .66f, y + h * .46f, x + w * .5f, y + h * .60f);
                g.DrawLine(pen, x + w * .26f, y + h * .74f, x + w * .74f, y + h * .74f);
                break;
            case 3:   // close
                g.DrawLine(pen, x + w * .32f, y + h * .32f, x + w * .68f, y + h * .68f);
                g.DrawLine(pen, x + w * .68f, y + h * .32f, x + w * .32f, y + h * .68f);
                break;
        }
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

    private static string FmtRpm(int rpm) => rpm >= 1000 ? $"{rpm / 1000f:0.0}k" : rpm.ToString();

    private static string FmtDur(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min"
        : $"{t.Seconds} s";

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

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
